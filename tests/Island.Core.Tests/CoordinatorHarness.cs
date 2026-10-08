using Island.Core.Abstractions;
using Island.Core.Application;
using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.Models;

namespace Island.Core.Tests;

/// <summary>Wires a coordinator to fakes and a manual clock. Call <see cref="Start"/> after any pre-seeding.</summary>
internal sealed class CoordinatorHarness : IDisposable
{
    public IslandSettings Settings { get; set; } = new();
    public FakeMediaService Media { get; } = new();
    public FakeVolumeService Volume { get; } = new();
    public FakeDisplayService Display { get; } = new();
    public ManualScheduler Scheduler { get; } = new();
    public List<IslandState> Emitted { get; } = new();
    public IslandCoordinator Coordinator { get; }

    public CoordinatorHarness(IIslandScheduler? scheduler = null)
    {
        Coordinator = new IslandCoordinator(Media, Volume, Display, scheduler ?? Scheduler, () => Settings);
        Coordinator.StateChanged += state => Emitted.Add(state);
    }

    public IslandState State => Coordinator.State;

    public IslandMode Mode => Coordinator.State.Mode;

    /// <summary>Starts the coordinator and clears the seed emission, so <see cref="Emitted"/> holds only later changes.</summary>
    public void Start()
    {
        Coordinator.Start();
        Emitted.Clear();
    }

    public void Advance(double seconds) => Scheduler.Advance(TimeSpan.FromSeconds(seconds));

    public void Post(IslandEvent e) => Coordinator.Post(e);

    /// <summary>Mode of every emitted state, in emission order.</summary>
    public List<IslandMode> EmittedModes() => Emitted.Select(s => s.Mode).ToList();

    public void Dispose() => Coordinator.Dispose();

    public static MediaInfo Track(string title, bool playing = true, byte[]? thumbnail = null) =>
        new(title, "Artist", thumbnail, playing, TimeSpan.Zero, TimeSpan.FromMinutes(3), "Spotify");
}

/// <summary>Scheduler that keeps every callback so tests can invoke stale ones directly.</summary>
internal sealed class RecordingScheduler : IIslandScheduler
{
    public sealed class Scheduled : IDisposable
    {
        public required TimeSpan Delay { get; init; }
        public required Action Callback { get; init; }
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    public List<Scheduled> All { get; } = new();

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var scheduled = new Scheduled { Delay = delay, Callback = callback };
        All.Add(scheduled);
        return scheduled;
    }
}
