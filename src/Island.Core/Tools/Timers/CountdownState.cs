namespace Island.Core.Tools.Timers;

public enum CountdownState
{
    /// <summary>Not started: the full duration is shown.</summary>
    Idle,
    Running,
    Paused,
    /// <summary>The time ran out. Starting again begins the full duration.</summary>
    Finished,
}
