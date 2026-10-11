using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Island.App.Widgets;
using Island.Core.Configuration;
using Island.Core.Progress;

namespace Island.App.ViewModels;

/// <summary>
/// The Suite's long tasks as the island shows them: the bar on the capsule edge with its percentage, the summary
/// text, and the rows of the Tasks widget. It has no timer of its own and does nothing while no task runs: it is
/// re-read only when <see cref="ProgressTracker.Changed"/> fires, coalesced by <see cref="UiSignal"/> (about four
/// refreshes a second at most).
/// UI thread only, except the tracker's event, which is marshalled through <see cref="UiSignal"/>.
/// </summary>
public sealed partial class ProgressPresenter : ObservableObject, IDisposable
{
    /// <summary>Coalescing window: a burst of progress updates costs one UI refresh.</summary>
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMilliseconds(250);

    /// <summary>The Tasks widget has room for three rows. The first rows of the snapshot get one; <see cref="CountText"/> still counts all.</summary>
    private const int MaxRows = 3;

    private readonly ProgressTracker _tracker;
    private readonly Func<IslandSettings> _settings;
    private readonly UiSignal _signal;
    private bool _disposed;

    /// <summary>At least one task is queued or running. False fades the capsule bar out.</summary>
    [ObservableProperty] private bool _isActive;
    /// <summary>Active tasks exist and none of them reports a percent: the bar pulses instead of filling.</summary>
    [ObservableProperty] private bool _isIndeterminate;
    /// <summary>Active and determinate: the capsule shows <see cref="PercentText"/> (when the pomodoro and idle content leave room).</summary>
    [ObservableProperty] private bool _hasPercentText;
    /// <summary>Mean progress of the active tasks that report a percent, 0..1.</summary>
    [ObservableProperty] private double _fraction;
    /// <summary>"42%", or empty when there is no percent to show.</summary>
    [ObservableProperty] private string _percentText = string.Empty;
    /// <summary>The app's reduce-animations setting or the Windows animation switch is on: the bar changes without motion.</summary>
    [ObservableProperty] private bool _reduceMotion;
    /// <summary>Spoken summary of the active tasks (accessibility), e.g. "2 tarefas em andamento, 42%".</summary>
    [ObservableProperty] private string _description = string.Empty;
    /// <summary>The Tasks widget has at least one row to show.</summary>
    [ObservableProperty] private bool _hasTasks;
    /// <summary>Header count of the Tasks widget: "2 ativas", or empty when nothing runs.</summary>
    [ObservableProperty] private string _countText = string.Empty;

    public ProgressPresenter(ProgressTracker tracker, Func<IslandSettings> settings)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _signal = new UiSignal(dispatcher, Refresh, RefreshWindow);
        _tracker.Changed += _signal.Signal;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Refresh();
    }

    /// <summary>Rows of the Tasks widget: active tasks first, then the recently finished ones. Updated in place by Id.</summary>
    public ObservableCollection<ProgressTaskRow> Tasks { get; } = new();

    /// <summary>Re-reads the motion preferences after settings change. Cheap and idempotent.</summary>
    public void RefreshMotionPreferences() => _signal.Signal();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tracker.Changed -= _signal.Signal;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        _signal.Dispose();
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e) => _signal.Signal();

    private void Refresh()
    {
        if (_disposed) return;

        ProgressSummary summary = _tracker.Summary;
        IslandSettings settings = _settings();
        // Same rule as the pomodoro countdown: the app setting or the Windows animation switch turns motion off.
        ReduceMotion = settings.ReduceAnimations || !SystemParameters.ClientAreaAnimation;

        IsActive = summary.HasActive;
        IsIndeterminate = summary.IsIndeterminate;
        Fraction = Math.Clamp(summary.Fraction, 0.0, 1.0);
        HasPercentText = IsActive && !IsIndeterminate;
        PercentText = HasPercentText ? FormatPercent(Fraction) : string.Empty;
        CountText = summary.ActiveCount switch
        {
            0 => string.Empty,
            1 => "1 ativa",
            _ => $"{summary.ActiveCount} ativas",
        };
        Description = Describe(summary);

        SyncTasks(_tracker.Snapshot());
        HasTasks = Tasks.Count > 0;
    }

    private static string Describe(ProgressSummary summary)
    {
        if (!summary.HasActive) return "Nenhuma tarefa em andamento";

        string tasks = summary.ActiveCount == 1
            ? "1 tarefa em andamento"
            : $"{summary.ActiveCount} tarefas em andamento";
        return summary.IsIndeterminate ? tasks : $"{tasks}, {FormatPercent(summary.Fraction)}";
    }

    private static string FormatPercent(double fraction) =>
        $"{(int)Math.Round(fraction * 100.0, MidpointRounding.AwayFromZero)}%";

    /// <summary>
    /// Matches the rows to the first <see cref="MaxRows"/> tasks by Id. A kept row is moved, never replaced, so its bar
    /// animates from its previous value; rows of tasks that left the snapshot are removed.
    /// </summary>
    private void SyncTasks(IReadOnlyList<ProgressTaskInfo> snapshot)
    {
        int shown = Math.Min(snapshot.Count, MaxRows);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < shown; i++) ids.Add(snapshot[i].Id);

        for (int i = Tasks.Count - 1; i >= 0; i--)
        {
            if (!ids.Contains(Tasks[i].Id)) Tasks.RemoveAt(i);
        }

        // After step i, Tasks[0..i] follow the snapshot order, so each insert or move lands at index i.
        for (int i = 0; i < shown; i++)
        {
            ProgressTaskInfo task = snapshot[i];
            int current = IndexOf(task.Id);
            ProgressTaskRow row;
            if (current < 0)
            {
                row = new ProgressTaskRow(task.Id);
                Tasks.Insert(i, row);
            }
            else
            {
                row = Tasks[current];
                if (current != i) Tasks.Move(current, i);
            }
            row.Apply(task);
        }
    }

    private int IndexOf(string id)
    {
        for (int i = 0; i < Tasks.Count; i++)
        {
            if (Tasks[i].Id == id) return i;
        }
        return -1;
    }
}
