using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SysGuard;

/// <summary>
/// Host do serviço gêmeo (<c>--watchdog-service</c>). É pequeno de propósito: um laço a cada 5 s
/// verifica se o serviço principal está RUNNING e o inicia de novo quando ele para. Não aplica camadas
/// sozinho — quem faz isso é o principal, que por sua vez também vigia este serviço.
/// </summary>
internal static class WatchdogHost
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

    /// <summary>Entrada do modo <c>--watchdog-service</c>. Devolve o código de erro do SCM ou 0.</summary>
    public static int Run()
    {
        var serviceName = Marshal.StringToHGlobalUni(AppInfo.WatchdogServiceName);
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
                    Log.Error($"Watchdog: StartServiceCtrlDispatcher falhou ({error}).");
                return error;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("Watchdog: falha ao iniciar o host do serviço.", ex);
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
            _statusHandle = RegisterServiceCtrlHandlerExW(AppInfo.WatchdogServiceName, HandlerCallback, IntPtr.Zero);
            if (_statusHandle == IntPtr.Zero)
            {
                Log.Error("Watchdog: RegisterServiceCtrlHandlerEx falhou.");
                return;
            }

            ReportStatus(ServiceStartPending, acceptedControls: 0);
            ReportStatus(ServiceRunning, ServiceAcceptStop | ServiceAcceptShutdown);
            Log.Info($"Watchdog iniciado. Versão {AppInfo.Version}.");

            ServiceWatchdog.RunLoop(
                AppInfo.ServiceName,
                ServiceWatchdog.CheckInterval,
                StopRequested,
                new ProcessCommandRunner());

            ReportStatus(ServiceStopPending, acceptedControls: 0);
            Log.Info("Watchdog parado (parada limpa; não reinicia o serviço principal por si só).");
            ReportStatus(ServiceStopped, 0);
        }
        catch (Exception ex)
        {
            Log.Error("Watchdog: ServiceMain falhou.", ex);
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
