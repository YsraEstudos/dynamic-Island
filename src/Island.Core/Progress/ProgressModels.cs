namespace Island.Core.Progress;

/// <summary>Where a long task stands. Mirrors the <c>state</c> field of the progress-link protocol (docs/progress-link.md).</summary>
public enum ProgressState
{
    Queued,
    Running,
    Done,
    Error,
    Cancelled,
}

/// <summary>Names shared with the sender.</summary>
public static class PipeNames
{
    /// <summary>Local named pipe the SuperPrograma Suite writes progress to.</summary>
    public const string Progress = "DynamicIsland.Progress.v1";
}

/// <summary>
/// One validated message from the sender. <paramref name="Percent"/> is 0..100, or null when the task cannot say
/// (indeterminate). Text fields are already trimmed to the protocol limits.
/// </summary>
public sealed record ProgressUpdate(
    string Id,
    string Title,
    string? Category,
    ProgressState State,
    double? Percent,
    string? Message,
    string? Source);

/// <summary>A task as the UI sees it: the last update plus what the tracker derives from it.</summary>
public sealed record ProgressTaskInfo(
    string Id,
    string Title,
    string? Category,
    ProgressState State,
    double? Percent,
    string? Message,
    DateTimeOffset StartedAt,
    TimeSpan? Eta)
{
    /// <summary>The task is still going (queued or running).</summary>
    public bool IsActive => State is ProgressState.Queued or ProgressState.Running;
}

/// <summary>Whole-island view of the running tasks, for the capsule bar.</summary>
/// <param name="ActiveCount">Queued plus running tasks.</param>
/// <param name="Fraction">0..1, the mean of the active tasks that report a percent; 0 when none does.</param>
/// <param name="IsIndeterminate">There are active tasks and none of them reports a percent.</param>
public readonly record struct ProgressSummary(int ActiveCount, double Fraction, bool IsIndeterminate)
{
    public static ProgressSummary None => new(0, 0.0, false);

    public bool HasActive => ActiveCount > 0;
}
