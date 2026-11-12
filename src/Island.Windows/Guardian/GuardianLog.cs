using System.Globalization;

namespace Island.Windows.Guardian;

/// <summary>
/// Best-effort append-only log for the hidden guardian process, which does not go through the Serilog startup of the
/// island. Located next to the island logs so a manual test can confirm the guardian ran. Never throws.
/// </summary>
internal sealed class GuardianLog
{
    private const long MaxBytes = 256 * 1024;

    private readonly object _gate = new();
    private readonly string? _path;

    public GuardianLog()
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DynamicIsland", "logs");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "guardian.log");
        }
        catch (Exception)
        {
            _path = null;
        }
    }

    public void Write(string message)
    {
        if (_path is null) return;
        try
        {
            lock (_gate)
            {
                Rotate();
                File.AppendAllText(_path,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [pid {Environment.ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Logging must never break the guard.
        }
    }

    private void Rotate()
    {
        try
        {
            var file = new FileInfo(_path!);
            if (file.Exists && file.Length > MaxBytes) File.Move(_path!, _path + ".1", overwrite: true);
        }
        catch (Exception)
        {
            // A failed rotation only means the file keeps growing.
        }
    }
}
