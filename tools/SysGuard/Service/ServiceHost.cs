using System.Runtime.InteropServices;

namespace SysGuard;

/// <summary>
/// Host do serviço do Windows via P/Invoke (sem pacote ServiceBase): registra o handler de controle,
/// informa o estado ao SCM e mantém o <see cref="GuardEngine"/> rodando até a parada.
/// </summary>
internal static class ServiceHost
{
    private const int ServiceWin32OwnProcess = 0x00000010;
    private const int ServiceAcceptStop = 0x00000001;
    private const int ServiceAcceptShutdown = 0x00000004;
    private const int ServiceControlStop = 1;
    private const int ServiceControlShutdown = 5;
    private const int ServiceStopped = 1;
    private const int ServiceStartPending = 2;
    private const int ServiceStopPending = 3;
    private const int ServiceRunning = 4;
    private const int ErrorFailedServiceControllerConnect = 1063;

    private static readonly ManualResetEvent StopRequested = new(false);
    private static readonly ServiceMainDelegate ServiceMainCallback = ServiceMain;
    private static readonly HandlerExDelegate HandlerCallback = Handler;
    private static IntPtr _statusHandle;
    private static GuardEngine? _engine;

    /// <summary>Entrada do modo <c>--service</c>. Devolve o código de erro do SCM ou 0.</summary>
    public static int Run()
    {
        var serviceName = Marshal.StringToHGlobalUni(AppInfo.ServiceName);
        try
        {
            var table = new ServiceTableEntry[2];
            table[0].ServiceName = serviceName;
            table[0].ServiceProc = Marshal.GetFunctionPointerForDelegate(ServiceMainCallback);
            table[1].ServiceName = IntPtr.Zero;
            table[1].ServiceProc = IntPtr.Zero;

            if (!StartServiceCtrlDispatcherW(table))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != ErrorFailedServiceControllerConnect)
                    Log.Error($"StartServiceCtrlDispatcher falhou ({error}).");
                return error;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao iniciar o host do serviço.", ex);
            return 1;
        }
        finally
        {
            Marshal.FreeHGlobal(serviceName);
        }
    }

    private static void ServiceMain(int argc, IntPtr argv)
    {
        try
        {
            _statusHandle = RegisterServiceCtrlHandlerExW(AppInfo.ServiceName, HandlerCallback, IntPtr.Zero);
            if (_statusHandle == IntPtr.Zero)
            {
                Log.Error("RegisterServiceCtrlHandlerEx falhou.");
                return;
            }

            ReportStatus(ServiceStartPending, acceptedControls: 0);

            if (ServiceInstaller.TryCompletePendingDelete())
            {
                ReportStatus(ServiceStopped, 0);
                return;
            }

            _engine = new GuardEngine(SystemState.CreateDefault(), AppInfo.ConfigPath);
            if (!_engine.Start())
            {
                ReportStatus(ServiceStopped, 0);
                return;
            }

            ReportStatus(ServiceRunning, ServiceAcceptStop | ServiceAcceptShutdown);

            // Vigia mútuo: se o serviço gêmeo parar (inclusive por sc stop), o principal o reinicia.
            _ = Task.Run(() => ServiceWatchdog.RunLoop(
                AppInfo.WatchdogServiceName, TimeSpan.FromSeconds(10), StopRequested, new ProcessCommandRunner()));

            StopRequested.WaitOne();

            ReportStatus(ServiceStopPending, acceptedControls: 0);
            _engine.Stop();
            _engine.Dispose();
            _engine = null;
            ReportStatus(ServiceStopped, 0);
        }
        catch (Exception ex)
        {
            Log.Error("ServiceMain falhou.", ex);
            try
            {
                _engine?.Stop();
                _engine?.Dispose();
            }
            catch
            {
                // Melhor esforço ao encerrar com erro.
            }

            if (_statusHandle != IntPtr.Zero) ReportStatus(ServiceStopped, 0);
        }
    }

    private static int Handler(uint control, uint eventType, IntPtr eventData, IntPtr context)
    {
        switch (control)
        {
            case ServiceControlStop:
            case ServiceControlShutdown:
                StopRequested.Set();
                break;
        }

        return 0;
    }

    private static void ReportStatus(int state, int acceptedControls)
    {
        if (_statusHandle == IntPtr.Zero) return;

        var status = new ServiceStatus
        {
            ServiceType = ServiceWin32OwnProcess,
            CurrentState = state,
            ControlsAccepted = acceptedControls,
            Win32ExitCode = 0,
            ServiceSpecificExitCode = 0,
            CheckPoint = 0,
            WaitHint = state is ServiceStartPending or ServiceStopPending ? 15_000 : 0,
        };
        SetServiceStatus(_statusHandle, ref status);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceTableEntry
    {
        public IntPtr ServiceName;
        public IntPtr ServiceProc;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public int ServiceType;
        public int CurrentState;
        public int ControlsAccepted;
        public int Win32ExitCode;
        public int ServiceSpecificExitCode;
        public int CheckPoint;
        public int WaitHint;
    }

    private delegate void ServiceMainDelegate(int argc, IntPtr argv);

    private delegate int HandlerExDelegate(uint control, uint eventType, IntPtr eventData, IntPtr context);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartServiceCtrlDispatcherW([In] ServiceTableEntry[] serviceTable);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr RegisterServiceCtrlHandlerExW(
        string serviceName, HandlerExDelegate handler, IntPtr context);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetServiceStatus(IntPtr statusHandle, ref ServiceStatus status);
}
