namespace Island.Core.Tools.Timers;

/// <summary>One lap: its own duration and the stopwatch time at which it ended.</summary>
public sealed record LapRecord(int Number, TimeSpan LapTime, TimeSpan TotalTime);
