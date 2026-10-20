using System.Text;
using System.Text.Json;
using Island.Windows.Storage;
using Island.Core.Abstractions;
using Island.Core.Configuration;

namespace Island.Windows.Configuration;

public sealed class JsonSettingsStore : ISettingsStore
{
    private const string FileName = "settings.json";
    /// <summary>Longest city name kept; the weather lookup is a free-text search, so a pasted essay is cut short.</summary>
    private const int MaxWeatherCityLength = 80;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _directory;
    private readonly string _path;
    private readonly object _gate = new();
    private Exception? _loadError;
    private bool _credentialUnavailable;

    /// <param name="directory">Defaults to %LocalAppData%\DynamicIsland.</param>
    public JsonSettingsStore(string? directory = null)
    {
        _directory = directory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland");
        _path = Path.Combine(_directory, FileName);
    }

    /// <summary>Returns defaults when the file is missing or unreadable. A corrupt file is moved to settings.json.bad. Never throws.</summary>
    public IslandSettings Load()
    {
        lock (_gate)
        {
            _loadError = null;
            _credentialUnavailable = false;

            string json;
            try
            {
                json = File.ReadAllText(_path, Encoding.UTF8);
            }
            catch (FileNotFoundException) { return new IslandSettings(); }
            catch (DirectoryNotFoundException) { return new IslandSettings(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _loadError = ex;
                // Transient (e.g. file locked): keep the file, use defaults for this run.
                return new IslandSettings();
            }

            IslandSettings? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<IslandSettings>(json, ReadOptions);
            }
            catch (Exception)
            {
                parsed = null;
            }

            if (parsed is null)
            {
                Quarantine();
                return new IslandSettings();
            }

            try { return Sanitize(parsed with { PhoneFcmToken = UserSecret.Unprotect(parsed.PhoneFcmToken ?? string.Empty) }); }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
            {
                _loadError = ex;
                _credentialUnavailable = true;
                return Sanitize(parsed with { PhoneFcmToken = string.Empty });
            }
        }
    }

    /// <summary>Atomic write: serialize to settings.json.tmp, then replace the target. Throws on I/O failure.</summary>
    public void Save(IslandSettings settings)
    {
        var clean = Sanitize(settings) with { PhoneFcmToken = UserSecret.Protect(settings.PhoneFcmToken ?? string.Empty) };
        lock (_gate)
        {
            if (_loadError is not null && !(_credentialUnavailable && !string.IsNullOrWhiteSpace(settings.PhoneFcmToken))) throw new IOException("Settings could not be read; writes are disabled until a successful reload.", _loadError);
            AtomicJsonFile.Write(_path, clean, WriteOptions);
            _loadError = null;
            _credentialUnavailable = false;
        }
    }

    private void Quarantine()
    {
        try { AtomicJsonFile.Quarantine(_path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _loadError = ex; }
    }



    /// <summary>Clamps numeric settings to ranges the UI can render. Non-finite seconds fall back to defaults.</summary>
    internal static IslandSettings Sanitize(IslandSettings s)
    {
        var d = new IslandSettings();
        return Island.Core.Pomodoro.PomodoroProfiles.Normalize(s with
        {
            MonitorIndex = Math.Max(0, s.MonitorIndex),
            CompactWidth = Math.Clamp(s.CompactWidth, 40, 1000),
            CompactHeight = Math.Clamp(s.CompactHeight, 16, 400),
            TopMargin = Math.Clamp(s.TopMargin, 0, 500),
            Dock = Enum.IsDefined(s.Dock) ? s.Dock : DockEdge.None,
            PositionX = Math.Clamp(s.PositionX, -100_000, 100_000),
            PositionY = Math.Clamp(s.PositionY, -100_000, 100_000),
            GameProcesses = (s.GameProcesses ?? Array.Empty<string>())
                .Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).ToList(),
            VolumeDisplaySeconds = ClampSeconds(s.VolumeDisplaySeconds, d.VolumeDisplaySeconds),
            MediaPreviewSeconds = ClampSeconds(s.MediaPreviewSeconds, d.MediaPreviewSeconds),
            NoticeSeconds = ClampSeconds(s.NoticeSeconds, d.NoticeSeconds),
            PomodoroFocusMinutes = Math.Clamp(s.PomodoroFocusMinutes, 1, 120),
            PomodoroBreakMinutes = Math.Clamp(s.PomodoroBreakMinutes, 1, 120),
            PomodoroCycles = Math.Clamp(s.PomodoroCycles, 1, 24),
            PomodoroFocusSound = Enum.IsDefined(s.PomodoroFocusSound) ? s.PomodoroFocusSound : d.PomodoroFocusSound,
            PomodoroBreakSound = Enum.IsDefined(s.PomodoroBreakSound) ? s.PomodoroBreakSound : d.PomodoroBreakSound,
            ClipboardMaxItems = Math.Clamp(s.ClipboardMaxItems, 1, 500),
            UpdateRepository = string.IsNullOrWhiteSpace(s.UpdateRepository) ? d.UpdateRepository : s.UpdateRepository.Trim(),
            WeatherCity = CleanCity(s.WeatherCity),
        });
    }

    /// <summary>Trimmed and cut to <see cref="MaxWeatherCityLength"/>. A null city is empty.</summary>
    private static string CleanCity(string? city)
    {
        string trimmed = (city ?? string.Empty).Trim();
        return trimmed.Length > MaxWeatherCityLength ? trimmed[..MaxWeatherCityLength] : trimmed;
    }

    private static double ClampSeconds(double value, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, 0.1, 3600.0) : fallback;
}
