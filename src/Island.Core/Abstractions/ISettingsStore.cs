using Island.Core.Configuration;

namespace Island.Core.Abstractions;

public interface ISettingsStore
{
    IslandSettings Load();
    void Save(IslandSettings settings);
}
