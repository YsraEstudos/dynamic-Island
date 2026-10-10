using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Island.App.Views;
using Island.App.Widgets;
using Island.Core.Application;
using Island.Core.Configuration;
using Island.Core.Idle;
using Island.Core.Models;
using Island.Core.Pomodoro;

namespace Island.App.ViewModels;

/// <summary>
/// What the compact capsule shows while it is idle: the weather, or the start time of a scheduled pomodoro.
/// It has no timer of its own. It is re-evaluated when something it depends on changes (island state, pomodoro, schedule,
/// weather, settings), and it asks the weather for a reading only while the island is idle.
/// UI thread only, except <see cref="WeatherRefresher.Changed"/>, which is marshalled through <see cref="UiSignal"/>.
/// </summary>
public sealed partial class IdlePresenter : ObservableObject, IDisposable
{
    /// <summary>Opacity of a reading the refresh has not renewed lately.</summary>
    private const double FadedOpacity = 0.45;

    private readonly IslandCoordinator _coordinator;
    private readonly PomodoroTimer _pomodoro;
    private readonly PomodoroSchedule _schedule;
    private readonly WeatherRefresher _weather;
    private readonly Func<IslandSettings> _settings;
    private readonly UiSignal _weatherSignal;
    private bool _disposed;

    [ObservableProperty] private bool _hasContent;
    /// <summary>The reading is a weather one (it shows the rain chance). False for the scheduled start.</summary>
    [ObservableProperty] private bool _isWeather;
    [ObservableProperty] private Geometry? _glyph;
    /// <summary>The temperature, or the start time of the scheduled pomodoro.</summary>
    [ObservableProperty] private string _primaryText = string.Empty;
    [ObservableProperty] private string _rainText = string.Empty;
    [ObservableProperty] private double _contentOpacity = 1.0;
    /// <summary>Spoken name of the whole idle content (accessibility).</summary>
    [ObservableProperty] private string _description = string.Empty;

    public IdlePresenter(IslandCoordinator coordinator, PomodoroTimer pomodoro, PomodoroSchedule schedule,
        WeatherRefresher weather, Func<IslandSettings> settings)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _pomodoro = pomodoro ?? throw new ArgumentNullException(nameof(pomodoro));
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _weather = weather ?? throw new ArgumentNullException(nameof(weather));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _weatherSignal = new UiSignal(dispatcher, Refresh);
        _weather.Changed += _weatherSignal.Signal;
    }

    /// <summary>
    /// Recomputes the idle content and tells the weather whether it is wanted: idle starts or stops its refresh cycle,
    /// and a changed city is passed on. Cheap and idempotent, so callers may call it on every relevant change.
    /// </summary>
    public void Refresh()
    {
        if (_disposed) return;

        IslandSettings settings = _settings();
        IslandState state = _coordinator.State;
        // The city goes first: a change drops the old reading, and the reading read below must be the new city's.
        _weather.SetCity(settings.WeatherCity);

        IdleInputs inputs = new(
            IdleModeEnabled: settings.IdleModeEnabled,
            IsCompact: state.Mode == IslandMode.Compact,
            Suspended: state.Suspended,
            // A paused plan counts as in progress: the capsule keeps its usual content until the plan ends.
            PomodoroBusy: _pomodoro.IsRunning || _pomodoro.PlanActive,
            MediaPlaying: state.Media?.IsPlaying == true,
            Scheduled: _schedule.Pending,
            Weather: _weather.Current,
            Now: DateTimeOffset.Now);

        _weather.SetIdle(IdleContentResolver.IsIdle(inputs));

        Show(IdleContentResolver.Resolve(inputs));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _weather.Changed -= _weatherSignal.Signal;
        _weatherSignal.Dispose();
    }

    private void Show(IdleContent content)
    {
        switch (content)
        {
            case IdleContent.Weather weather:
                ShowWeather(weather);
                break;
            case IdleContent.ScheduledPomodoro scheduled:
                ShowScheduled(scheduled.Start);
                break;
            default:
                Clear();
                break;
        }
    }

    private void ShowWeather(IdleContent.Weather weather)
    {
        WeatherSnapshot snapshot = weather.Snapshot;
        WeatherCodeMapper.Condition condition = WeatherCodeMapper.Map(snapshot.WeatherCode);
        int rain = snapshot.RainChancePercent;

        HasContent = true;
        IsWeather = true;
        Glyph = IdleIcons.For(condition.Category);
        // Whole degrees: the capsule has no room for decimals. Integers print the same in every culture here.
        PrimaryText = $"{(int)Math.Round(snapshot.TemperatureC)}°C";
        RainText = $"{rain}%";
        ContentOpacity = weather.Faded ? FadedOpacity : 1.0;
        Description = weather.Faded
            ? $"{condition.Description}, {rain}% de chance de chuva (última leitura)"
            : $"{condition.Description}, {rain}% de chance de chuva";
    }

    private void ShowScheduled(ScheduledStart start)
    {
        string time = start.At.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

        HasContent = true;
        IsWeather = false;
        Glyph = IdleIcons.Timer;
        PrimaryText = time;
        RainText = string.Empty;
        ContentOpacity = 1.0;
        Description = start.Mode == ScheduledStartMode.Angry
            ? $"Pomodoro raivoso começa às {time}"
            : $"Pomodoro começa às {time}";
    }

    private void Clear()
    {
        HasContent = false;
        IsWeather = false;
        Glyph = null;
        PrimaryText = string.Empty;
        RainText = string.Empty;
        ContentOpacity = 1.0;
        Description = string.Empty;
    }
}
