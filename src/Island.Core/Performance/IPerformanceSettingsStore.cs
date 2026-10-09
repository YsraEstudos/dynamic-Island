namespace Island.Core.Performance;

public interface IPerformanceSettingsStore
{
    /// <summary>Never throws: a missing or corrupt file yields <see cref="PerformanceAlertSettings.Default"/>.</summary>
    PerformanceAlertSettings Load();

    void Save(PerformanceAlertSettings settings);
}
