using System.Text.Json;
using Island.Windows.Storage;
using Island.Core.Performance;

namespace Island.Windows.Performance;

/// <summary>Alert settings in %LocalAppData%\DynamicIsland\performance.json, written atomically (.tmp + move).</summary>
public sealed class JsonPerformanceSettingsStore : IPerformanceSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;

    public JsonPerformanceSettingsStore(string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland");
        _path = Path.Combine(directory, "performance.json");
    }

    public PerformanceAlertSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return PerformanceAlertSettings.Default;
            PerformanceAlertSettings? settings = JsonSerializer.Deserialize<PerformanceAlertSettings>(File.ReadAllText(_path), Options);
            return settings is null ? PerformanceAlertSettings.Default : settings.Sanitized();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return PerformanceAlertSettings.Default;
        }
    }

    public void Save(PerformanceAlertSettings settings) => AtomicJsonFile.Write(_path, settings.Sanitized(), Options);
}
