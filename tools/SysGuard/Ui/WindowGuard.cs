using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SysGuard;

/// <summary>
/// Última camada do bloqueio: minimiza janelas de navegador cujo título denuncia conteúdo adulto,
/// inclusive abas abertas em segundo plano, e — durante toda a sessão do Pomodoro Angry do Dynamic
/// Island — janelas cujo título cita YouTube, Twitter/X, Instagram ou Reddit. Nos 60 s finais de um
/// intervalo rodando, só Twitter/X e Instagram deixam de ser minimizados; YouTube e Reddit continuam.
/// Roda no processo da bandeja (sessão do usuário); o bloqueio de rede continua no serviço mesmo que
/// este processo seja encerrado.
/// </summary>
internal sealed class WindowGuard : IDisposable
{
    private const int SwMinimize = 6;
    private const int FullScanEveryTicks = 3;

    private readonly Func<bool> _enabled;
    private readonly AngryLiveCache _angryLive;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Dictionary<uint, (string ProcessName, long CheckedAt)> _processNames = new();
    private readonly EnumWindowsProc _enumCallback;
    private int _ticks;
    private bool _protectionOn;
    private bool _angryGuardOn;
    private bool _angryRelease;
    private bool _disposed;

    public WindowGuard(Func<bool> enabled, AngryLiveCache? angryLive = null)
    {
        _enabled = enabled;
        _angryLive = angryLive ?? AngryLiveCache.ForCurrentUser;
        _enumCallback = OnWindow;
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => Tick();
    }

    public void Start() => _timer.Start();

    private void Tick()
    {
        if (_disposed) return;

        try
        {
            var now = DateTimeOffset.UtcNow;
            _protectionOn = _enabled();
            var angry = _angryLive.Read(now);
            _angryGuardOn = angry.IsGuarding(now);
            _angryRelease = angry.IsBreakRelease(now);
            if (!_protectionOn && !_angryGuardOn) return;

            _ticks++;
            CheckWindow(GetForegroundWindow());
            if (_ticks % FullScanEveryTicks == 0) EnumWindows(_enumCallback, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Error("Guard de janelas falhou.", ex);
        }
    }

    private bool OnWindow(IntPtr hwnd, IntPtr parameter)
    {
        if (!IsWindowVisible(hwnd)) return true;
        if (GetWindowTextLength(hwnd) == 0) return true;
        if (hwnd == GetForegroundWindow()) return true; // já verificado neste ciclo
        CheckWindow(hwnd);
        return true;
    }

    private void CheckWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        string? processName = ProcessNameFor(hwnd);
        if (!AdultContent.IsBrowserProcess(processName)) return;

        var title = WindowTitle(hwnd);
        if (_protectionOn && AdultContent.IsAdultTitle(title))
        {
            Minimize(hwnd, "conteúdo bloqueado");
            return;
        }

        if (_angryGuardOn && AngryTitles.IsDistractionTitle(title)
            && !(_angryRelease && AngryTitles.IsReleasedTitle(title)))
        {
            Minimize(hwnd, "distração do Angry");
        }
    }

    private void Minimize(IntPtr hwnd, string reason)
    {
        // O estado pode ter mudado no meio da varredura: não minimiza se nada está mais ativo.
        if (!_protectionOn && !_angryGuardOn) return;

        ShowWindowAsync(hwnd, SwMinimize);
        Log.Info($"Janela de navegador minimizada ({reason}).");
    }

    private string? ProcessNameFor(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId) return null;

        long now = Environment.TickCount64;
        if (_processNames.TryGetValue(pid, out var cached) && now - cached.CheckedAt < 30_000)
            return cached.ProcessName;

        try
        {
            using var process = Process.GetProcessById((int)pid);
            var name = process.ProcessName;
            _processNames[pid] = (name, now);
            if (_processNames.Count > 256) _processNames.Clear();
            return name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string WindowTitle(IntPtr hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        if (length <= 0) return string.Empty;

        var buffer = new StringBuilder(length + 1);
        GetWindowTextW(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
}
