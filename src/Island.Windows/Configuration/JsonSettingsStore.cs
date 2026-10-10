using System.Text;
using System.Text.Json;
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
    private readonly string _tempPath;
    private readonly string _badPath;
    private readonly object _gate = new();

    /// <param name="directory">Defaults to %LocalAppData%\DynamicIsland.</param>
    public JsonSettingsStore(string? directory = null)
    {
        _directory = directory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland");
        _path = Path.Combine(_directory, FileName);
        _tempPath = _path + ".tmp";
        _badPath = _path + ".bad";
    }

    /// <summary>Returns defaults when the file is missing or unreadable. A corrupt file is moved to settings.json.bad. Never throws.</summary>
    public IslandSettings Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return new IslandSettings();

            string json;
            try
            {
                json = File.ReadAllText(_path, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
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

            return Sanitize(parsed);
        }
    }

    /// <summary>Atomic write: serialize to settings.json.tmp, then replace the target. Throws on I/O failure.</summary>
    public void Save(IslandSettings settings)
    {
        var clean = Sanitize(settings);

        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            try
            {
                using (var stream = new FileStream(_tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, clean, WriteOptions);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(_path))
                    File.Replace(_tempPath, _path, destinationBackupFileName: null);
                else
                    File.Move(_tempPath, _path, overwrite: true);
            }
            finally
            {
                TryDelete(_tempPath);
            }
        }
    }

    private void Quarantine()
    {
        try
        {
            File.Move(_path, _badPath, overwrite: true);
        }
        catch (Exception)
        {
            // If the move fails the corrupt file stays in place. The next Save overwrites it.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // Best-effort cleanup of a leftover temp file.
        }
    }

    /// <summary>Clamps numeric settings to ranges the UI can render. Non-finite seconds fall back to defaults.</summary>
    internal static IslandSettings Sanitize(IslandSettings s)
    {
        var d = new IslandSettings();
        return s with
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
            ExpandedIdleSeconds = ClampSeconds(s.ExpandedIdleSeconds, d.ExpandedIdleSeconds),
            UpdateRepository = string.IsNullOrWhiteSpace(s.UpdateRepository) ? d.UpdateRepository : s.UpdateRepository.Trim(),
            WeatherCity = CleanCity(s.WeatherCity),
        };
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
