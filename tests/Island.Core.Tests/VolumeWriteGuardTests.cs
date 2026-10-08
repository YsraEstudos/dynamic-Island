using Island.Core.Abstractions;
using Island.Core.Application;
using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.Models;

namespace Island.Core.Tests;

/// <summary>
/// Guards the rule that the island only reads system volume: values reported by the audio service
/// update the UI state, and nothing in the coordinator writes volume back (no startup write, no echo, no loop).
/// </summary>
public class VolumeWriteGuardTests
{
    private sealed class SpyVolumeService : IVolumeService
    {
        public event EventHandler<VolumeInfo>? VolumeChanged;

        public VolumeInfo Current { get; private set; } = new(50, false);

        public int InitializeCalls { get; private set; }
        public int SetLevelCalls { get; private set; }
        public int SetMutedCalls { get; private set; }

        public void Initialize() => InitializeCalls++;

        public void SetLevel(int level0to100) => SetLevelCalls++;

        public void SetMuted(bool muted) => SetMutedCalls++;

        /// <summary>Simulates the system reporting a master volume or mute change.</summary>
        public void Report(VolumeInfo info)
        {
            Current = info;
            VolumeChanged?.Invoke(this, info);
        }

        public void Dispose() { }
    }

    [Fact]
    public void Starting_the_coordinator_does_not_write_volume()
    {
        var volume = new SpyVolumeService();
        using var coordinator = CreateCoordinator(volume, new IslandSettings());

        coordinator.Start();

        Assert.Equal(0, volume.InitializeCalls);
        Assert.Equal(0, volume.SetLevelCalls);
        Assert.Equal(0, volume.SetMutedCalls);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(50, true)]
    [InlineData(99, false)]
    [InlineData(100, true)]
    public void System_volume_reports_update_state_without_writing_back(int level, bool muted)
    {
        var volume = new SpyVolumeService();
        using var coordinator = CreateCoordinator(volume, new IslandSettings());
        coordinator.Start();

        volume.Report(new VolumeInfo(level, muted));

        Assert.Equal(new VolumeInfo(level, muted), coordinator.State.Volume);
        Assert.Equal(0, volume.SetLevelCalls);
        Assert.Equal(0, volume.SetMutedCalls);
    }

    [Fact]
    public void Repeated_system_reports_never_write_even_with_the_volume_overlay_hidden()
    {
        var volume = new SpyVolumeService();
        using var coordinator = CreateCoordinator(volume, new IslandSettings { ShowVolume = false });
        coordinator.Start();

        for (var i = 0; i <= 100; i++)
            volume.Report(new VolumeInfo(i, i % 2 == 0));

        Assert.Equal(new VolumeInfo(100, true), coordinator.State.Volume);
        Assert.Equal(0, volume.SetLevelCalls);
        Assert.Equal(0, volume.SetMutedCalls);
    }

    private static IslandCoordinator CreateCoordinator(IVolumeService volume, IslandSettings settings) =>
        new(new FakeMediaService(), volume, new FakeDisplayService(), new ManualScheduler(), () => settings);
}
