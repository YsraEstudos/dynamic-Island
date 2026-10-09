using Island.Core.Performance;
using Island.Windows.Performance;

namespace Island.Windows.Tests.Performance;

public sealed class JsonPerformanceSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "DynamicIslandPerformanceTests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "performance.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Save_then_Load_roundtrips_the_alert_settings()
    {
        var store = new JsonPerformanceSettingsStore(_dir);

        store.Save(new PerformanceAlertSettings(false, 88, 79));

        Assert.Equal(new PerformanceAlertSettings(false, 88, 79), new JsonPerformanceSettingsStore(_dir).Load());
    }

    [Fact]
    public void Missing_file_loads_the_defaults()
    {
        Assert.Equal(PerformanceAlertSettings.Default, new JsonPerformanceSettingsStore(_dir).Load());
    }

    [Fact]
    public void Corrupt_file_loads_the_defaults_instead_of_throwing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");

        Assert.Equal(PerformanceAlertSettings.Default, new JsonPerformanceSettingsStore(_dir).Load());
    }

    [Fact]
    public void Out_of_range_limits_in_the_file_are_brought_into_range()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ \"enabled\": true, \"cpuLimitC\": 5, \"gpuLimitC\": 500 }");

        Assert.Equal(new PerformanceAlertSettings(true, 60, 105), new JsonPerformanceSettingsStore(_dir).Load());
    }
}
