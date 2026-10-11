using Island.Core.Application;
using Island.Core.Models;
using Island.Core.Progress;

namespace Island.App.Composition;

/// <summary>
/// Connects the progress tracker to the island at startup: a finished or failed task becomes a normal notice, and the
/// tracker starts listening to its feed. Running tasks reach the capsule and the Tasks widget through the view model.
/// </summary>
public static class ProgressWiring
{
    public static void Attach(ProgressTracker tracker, IslandCoordinator coordinator)
    {
        tracker.NoticeRaised += notice => coordinator.Post(new IslandEvent.NoticeRaised(notice));
        tracker.Start();
    }
}
