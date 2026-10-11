using CommunityToolkit.Mvvm.ComponentModel;
using Island.Core.Progress;

namespace Island.App.ViewModels;

/// <summary>
/// One row of the Tasks widget. A row is created once per task Id and then updated in place, so its bar springs from
/// the last value instead of restarting. UI thread only.
/// </summary>
public sealed partial class ProgressTaskRow : ObservableObject
{
    public ProgressTaskRow(string id)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
    }

    /// <summary>The sender's task id (the row key).</summary>
    public string Id { get; }

    [ObservableProperty] private string _title = string.Empty;
    /// <summary>The sender's message plus the time left ("faltam 1:20") when known; otherwise the state in words.</summary>
    [ObservableProperty] private string _detail = string.Empty;
    /// <summary>0..1, the bar's target.</summary>
    [ObservableProperty] private double _fraction;
    /// <summary>"42%", or empty while the percent is unknown.</summary>
    [ObservableProperty] private string _percentText = string.Empty;
    /// <summary>Running with no percent yet: the bar pulses.</summary>
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private bool _isError;

    /// <summary>Copies the tracker's view of the task into the row. Setters raise only on a real change.</summary>
    public void Apply(ProgressTaskInfo task)
    {
        ArgumentNullException.ThrowIfNull(task);

        IsDone = task.State == ProgressState.Done;
        IsError = task.State == ProgressState.Error;

        // A finished task is full. An error keeps the last percent it reported (none if it never reported one).
        double? percent = null;
        if (IsDone) percent = 100.0;
        else if (task.Percent is { } reported) percent = Math.Clamp(reported, 0.0, 100.0);

        Title = task.Title;
        Fraction = percent is { } shown ? shown / 100.0 : 0.0;
        PercentText = percent is { } whole
            ? $"{(int)Math.Round(whole, MidpointRounding.AwayFromZero)}%"
            : string.Empty;
        IsIndeterminate = percent is null && task.IsActive;
        Detail = DetailFor(task);
    }

    private static string DetailFor(ProgressTaskInfo task)
    {
        bool hasMessage = !string.IsNullOrWhiteSpace(task.Message);
        if (task.Eta is { } eta && task.IsActive)
        {
            string left = $"faltam {FormatTime(eta)}";
            return hasMessage ? $"{task.Message} · {left}" : left;
        }
        if (hasMessage) return task.Message!;

        return task.State switch
        {
            ProgressState.Queued => "Na fila",
            ProgressState.Done => "Concluída",
            ProgressState.Error => "Falhou",
            ProgressState.Cancelled => "Cancelada",
            _ => string.Empty,
        };
    }

    /// <summary>m:ss, or h:mm:ss from one hour on. Rounds down, so the countdown never shows time that is already gone.</summary>
    private static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }
}
