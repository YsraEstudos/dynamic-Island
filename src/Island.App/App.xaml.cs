using System.Globalization;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Island.App.Composition;
using Island.App.Diagnostics;
using Island.App.Shell;
using Island.App.ViewModels;
using Island.App.Widgets;
using Island.Core.Abstractions;
using Island.Core.Application;
using Island.Core.Clipboard;
using Island.Core.Pomodoro;
using Island.Windows.Focus;
using Island.Windows.Input;
using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.Models;
using Island.Windows.Display;
using Island.Windows.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Serilog;

namespace Island.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? _guard;
    private ServiceProvider? _services;
    private IslandWindow? _window;
    private SettingsWindow? _settingsWindow;
    private TrayIconService? _tray;
    private DispatcherTimer? _demoTimer;
    private GlobalHotkey? _hotkey;
    private AngryPomodoro? _angry;
    private SoakRunner? _soak;
    private ISystemNoticeSource[] _systemNoticeSources = [];
    private IslandCoordinator? _noticeCoordinator;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _guard = SingleInstanceGuard.TryAcquire("DynamicIsland");
        if (_guard is null) { Shutdown(); return; }

        // Software rendering: the island is a small layered window, and the GPU path costs far more than it saves.
        // It loads the display driver (about 60 MB private memory and 1,200 handles here) and reads every frame back
        // from the GPU, which used more CPU than rasterizing the pill directly. Must be set before the first window.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "logs");
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(Path.Combine(logDir, "island-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 5)
            .CreateLogger();
        var loggerFactory = new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger);

        DispatcherUnhandledException += (_, ex) => { Log.Error(ex.Exception, "Unhandled UI exception"); ex.Handled = true; };

        TimeSpan? soak = ParseSoak(e.Args);
        bool demo = e.Args.Contains("--demo") || soak is not null;
        _services = ServiceRegistration.Build(demo, loggerFactory);
        var sp = _services;

        var holder = sp.GetRequiredService<SettingsHolder>();
        var monitors = sp.GetRequiredService<MonitorService>();
        var media = sp.GetRequiredService<IMediaService>();
        var volume = sp.GetRequiredService<IVolumeService>();
        var display = sp.GetRequiredService<IDisplayService>();
        var coordinator = sp.GetRequiredService<IslandCoordinator>();

        var applier = sp.GetRequiredService<SettingsApplier>();
        var vm = sp.GetRequiredService<IslandViewModel>();
        var updates = sp.GetRequiredService<IUpdateService>();
        _window = new IslandWindow(vm, monitors, sp.GetRequiredService<Func<IslandSettings>>(), applier.Apply, () => updates.Available);
        applier.Applied = () => Dispatcher.BeginInvoke(() => _window?.ApplySettings());
        _window.OpenSettingsRequested += () => OpenSettings(holder, applier, monitors);
        _window.QuitRequested += RequestQuit;
        _window.InstallUpdateRequested += () => _ = InstallUpdateAsync(updates, coordinator);
        _window.CheckUpdateRequested += () => _ = CheckUpdatesFromMenuAsync(updates, coordinator);
        _window.TaskbarRecreated += () => _tray?.Refresh();
        _window.Show();

        volume.Initialize();
        display.Start();
        try { await media.InitializeAsync(); }
        catch (Exception ex) { Log.Error(ex, "Media service failed to initialize"); }
        // The Mini pill is remembered: keep the saved flag in step with the island state (written only when it changes).
        coordinator.StateChanged += state =>
        {
            bool mini = state.Mode == IslandMode.Mini;
            if (mini == holder.Current.Minimized) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (mini != holder.Current.Minimized) applier.Apply(holder.Current with { Minimized = mini });
            });
        };
        coordinator.Start();

        _noticeCoordinator = coordinator;
        _systemNoticeSources = sp.GetServices<ISystemNoticeSource>().ToArray();
        foreach (var source in _systemNoticeSources)
        {
            source.NoticeRaised += OnSystemNoticeRaised;
            try { source.Start(); }
            catch (Exception ex) { Log.Warning(ex, "System notice source failed to start"); }
        }

        // Angry pomodoro: while locked, browsers on distracting sites are minimized; a blocked site raises a notice.
        var angry = sp.GetRequiredService<AngryPomodoro>();
        var siteGuard = sp.GetRequiredService<ForegroundSiteGuard>();
        _angry = angry;
        angry.LockChanged += () => Dispatcher.BeginInvoke(() => siteGuard.SetActive(angry.IsLocked));
        // The scheduled start is resolved here so it exists (and is disposed with the container) from launch.
        sp.GetRequiredService<PomodoroSchedule>();
        siteGuard.SiteBlocked += site => coordinator.Post(new IslandEvent.NoticeRaised(
            new Notice($"{site} bloqueado", "Pomodoro raivoso ativo", "timer")));

        // Each locked focus of an angry plan -> ask the phone to block for the time left of that focus. Breaks are free,
        // so the phone is released when the block expires. Leaving Angry early sends nothing: the phone keeps its block
        // until the original end (or until the phrase is typed on the phone). LockChanged also fires for session changes
        // that are not a new lock, so only a false -> true edge of IsLocked sends; the lock guards the edge state.
        var phone = sp.GetRequiredService<IPhoneBlockNotifier>();
        var focusTimer = sp.GetRequiredService<PomodoroTimer>();
        bool wasLocked = false;
        object phoneEdgeGate = new();
        angry.LockChanged += () =>
        {
            bool sendBlock;
            lock (phoneEdgeGate)
            {
                bool locked = angry.IsLocked;
                sendBlock = locked && !wasLocked && holder.Current.PhoneBlockEnabled;
                wasLocked = locked;
            }
            if (!sendBlock) return;
            _ = BlockPhoneAsync(phone, coordinator, focusTimer.Remaining, holder.Current.PhoneFcmToken);
        };

        // Pomodoro -> toast (and sound when enabled), clipboard capture -> history, global hotkey -> clipboard panel
        var pomodoro = sp.GetRequiredService<PomodoroTimer>();
        pomodoro.Transitioned += t =>
        {
            coordinator.Post(new IslandEvent.NoticeRaised(PomodoroNotices.For(t)));
            if (holder.Current.PomodoroSound) PlayPomodoroSound();
        };
        var clipboard = sp.GetRequiredService<IClipboardService>();
        var history = sp.GetRequiredService<ClipboardHistory>();
        // Captures go through one background chain, in order, so screenshots are compressed off the clipboard thread.
        Task clipboardChain = Task.CompletedTask;
        object clipboardGate = new();
        clipboard.ItemCaptured += (_, item) =>
        {
            if (!holder.Current.ClipboardEnabled) return;
            lock (clipboardGate)
            {
                clipboardChain = clipboardChain.ContinueWith(_ => history.Add(
                    item is { Kind: ClipboardKind.Image, ImageBytes: { } bytes }
                        ? item with { ImageBytes = ClipboardImageCodec.Compress(bytes) }
                        : item), TaskScheduler.Default);
            }
        };
        try { clipboard.Start(); } catch (Exception ex) { Log.Error(ex, "Clipboard service failed to start"); }
        updates.Start(); // Checks GitHub once shortly after startup, then every few hours. Demo mode does nothing.
        if (!demo)
        {
            _hotkey = new GlobalHotkey(GlobalHotkey.ModControl | GlobalHotkey.ModAlt, 0x56 /* V */);
            _hotkey.Pressed += () => coordinator.Post(new IslandEvent.ClipboardRequested());
            if (!_hotkey.Register()) Log.Warning("Ctrl+Alt+V is already taken; clipboard hotkey disabled");
        }
        if (demo && soak is null) StartDemo(sp);

        _tray = new TrayIconService(
            openSettings: () => OpenSettings(holder, applier, monitors),
            setPaused: paused => coordinator.Post(new IslandEvent.PausedChanged(paused)),
            exit: RequestQuit);

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // Soak test (--soak): fake events for the given duration, then shut down. Its verdict is in the log.
        if (soak is { } soakDuration)
        {
            _soak = new SoakRunner(coordinator, sp.GetRequiredService<FakeMediaService>(),
                sp.GetRequiredService<FakeVolumeService>(), Dispatcher, soakDuration,
                () => Dispatcher.BeginInvoke(() => Shutdown()));
            _soak.Start();
        }
    }

    /// <summary>Routes passive Windows signals through the coordinator's shared priorities and timer.</summary>
    private void OnSystemNoticeRaised(object? sender, Notice notice) =>
        _noticeCoordinator?.Post(new IslandEvent.NoticeRaised(notice));

    /// <summary>Fire-and-forget: the notifier never throws, and a failure only raises a toast. Angry keeps running either way.</summary>
    private static async Task BlockPhoneAsync(IPhoneBlockNotifier phone, IslandCoordinator coordinator, TimeSpan remaining, string token)
    {
        PhoneBlockOutcome outcome = await phone.NotifyAsync(remaining, token).ConfigureAwait(false);
        coordinator.Post(new IslandEvent.NoticeRaised(outcome.Delivered
            ? new Notice("Celular bloqueado", outcome.Detail, "timer")
            : new Notice("Celular não bloqueado", outcome.Detail, "timer")));
    }

    /// <summary>Runs on the timer thread. Audio failures are logged and ignored: the toast still shows.</summary>
    private static void PlayPomodoroSound()
    {
        try { SystemSounds.Asterisk.Play(); }
        catch (Exception ex) { Log.Debug(ex, "Pomodoro sound failed to play"); }
    }

    /// <summary>Quit from the island menu or the tray. While an angry pomodoro is locked, the unlock dialog must release it first.</summary>
    private void RequestQuit()
    {
        if (!ConfirmNotLocked()) return;
        Shutdown();
    }

    /// <summary>
    /// "Install update" from the island menu. Installing replaces the running app, so it passes the same Angry guard as Quit.
    /// Progress is shown as temporary notices; failures are logged and shown as a notice.
    /// </summary>
    private async Task InstallUpdateAsync(IUpdateService updates, IslandCoordinator coordinator)
    {
        UpdateInfo? update = updates.Available;
        if (update is null || !ConfirmNotLocked()) return;

        coordinator.Post(new IslandEvent.NoticeRaised(new Notice($"Downloading {update.Tag}", "Update", "timer")));
        try
        {
            await updates.InstallAsync(update);
            coordinator.Post(new IslandEvent.NoticeRaised(new Notice($"Installing {update.Tag}", "The app restarts by itself", "timer")));
            await Task.Delay(1500); // Let the notice show before the app exits so the helper can replace its files.
            Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Installing update {Tag} failed", update.Tag);
            coordinator.Post(new IslandEvent.NoticeRaised(new Notice("Update failed", "See the logs", "timer")));
        }
    }

    /// <summary>Asks the update source for a newer release and describes the outcome in one line. Never throws.</summary>
    private static async Task<string> CheckForUpdatesAsync(IUpdateService updates)
    {
        try
        {
            UpdateInfo? update = await updates.CheckAsync();
            return update is null ? "You're up to date." : $"{update.Tag} is available. Right-click the island and choose Install update.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check failed");
            return "Couldn't check for updates. Try again later.";
        }
    }

    /// <summary>"Check for updates" from the island menu: the outcome is a temporary notice.</summary>
    private static async Task CheckUpdatesFromMenuAsync(IUpdateService updates, IslandCoordinator coordinator)
    {
        coordinator.Post(new IslandEvent.NoticeRaised(new Notice("Checking for updates", "Update", "timer")));
        string result = await CheckForUpdatesAsync(updates);
        coordinator.Post(new IslandEvent.NoticeRaised(new Notice("Update", result, "timer")));
    }

    /// <summary>True when no angry session is locked, or when the unlock phrase was typed (which releases the lock).</summary>
    private bool ConfirmNotLocked() => _angry is not { IsSessionActive: true } || UnlockWindow.ShowFor(_angry);

    private void OpenSettings(SettingsHolder holder, SettingsApplier applier, MonitorService monitors)
    {
        if (_settingsWindow is null)
        {
            var names = monitors.GetMonitors()
                .Select(m => $"Monitor {m.Index + 1}{(m.IsPrimary ? " (primary)" : "")} - {m.Width}x{m.Height}")
                .ToList();
            var phone = _services!.GetRequiredService<IPhoneBlockNotifier>();
            var updates = _services!.GetRequiredService<IUpdateService>();
            var svm = new SettingsViewModel(holder.Current, names, applier.Apply, (span, token) => phone.NotifyAsync(span, token),
                () => CheckForUpdatesAsync(updates));
            _settingsWindow = new SettingsWindow(svm);
            // Closing really closes it, so its visual tree is freed instead of sitting hidden for the app's lifetime.
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.ShowOrActivate();
    }

    private void OnDisplayChanged(object? s, EventArgs e) => Dispatcher.BeginInvoke(() => _window?.Nudge());

    private void OnSessionSwitch(object? s, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock) Dispatcher.BeginInvoke(() => _window?.Nudge());
    }

    private void OnPowerModeChanged(object? s, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(() => _window?.Nudge());
    }

    /// <summary>Demo: a looping fake playlist plus periodic volume changes, so every state can be seen without real media.</summary>
    private void StartDemo(IServiceProvider sp)
    {
        var media = sp.GetRequiredService<FakeMediaService>();
        var vol = sp.GetRequiredService<FakeVolumeService>();
        media.SetPlaylist(
            new MediaInfo("Blue Hour", "Nova Coast", null, true, TimeSpan.FromSeconds(83), TimeSpan.FromSeconds(214), "demo"),
            new MediaInfo("Glass Tides", "Nova Coast", null, true, TimeSpan.Zero, TimeSpan.FromSeconds(187), "demo"));
        var hist = sp.GetRequiredService<ClipboardHistory>();
        hist.Add(ClipboardItem.FromText("Meet at the studio at four. Bring the new cut."));
        hist.Add(ClipboardItem.FromText("#1219ED"));
        hist.Add(ClipboardItem.FromText("https://weeknight.kitchen/nachos"));
        hist.Add(ClipboardItem.FromText("A better clipboard?"));
        int n = 0;
        _demoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _demoTimer.Tick += (_, _) =>
        {
            n++;
            media.Tick(TimeSpan.FromSeconds(1));
            if (n % 30 == 5) coordinatorNotice("Caps Lock ativado", "Letras maiúsculas", "caps-on");
            if (n % 30 == 10) coordinatorNotice("Caps Lock desativado", "Letras minúsculas", "caps-off");
            if (n % 30 == 15) coordinatorNotice("Bluetooth conectado", "Fones de ouvido", "bluetooth-on");
            if (n % 30 == 20) coordinatorNotice("Bluetooth desconectado", "Fones de ouvido", "bluetooth-off");
            if (n % 30 == 25) coordinatorNotice("USB conectado", "Dispositivo de armazenamento", "usb-on");
            if (n % 30 == 0) coordinatorNotice("USB removido", "Dispositivo de armazenamento", "usb-off");
            if (n % 12 == 0) vol.SetLevel(vol.Current.Level >= 90 ? 30 : vol.Current.Level + 15);
            if (n % 40 == 0) _ = media.NextAsync();
        };
        _demoTimer.Start();

        void coordinatorNotice(string title, string subtitle, string glyph) =>
            sp.GetRequiredService<IslandCoordinator>().Post(new IslandEvent.NoticeRaised(new Notice(title, subtitle, glyph)));
    }

    /// <summary>Parses <c>--soak</c> (10 minutes) or <c>--soak=minutes</c>. Returns null when no soak run is requested.</summary>
    private static TimeSpan? ParseSoak(string[] args)
    {
        const double DefaultMinutes = 10;
        foreach (string arg in args)
        {
            if (arg == "--soak") return TimeSpan.FromMinutes(DefaultMinutes);
            if (!arg.StartsWith("--soak=", StringComparison.OrdinalIgnoreCase)) continue;

            string value = arg["--soak=".Length..];
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes)
                && minutes > 0 && minutes <= 24 * 60)
            {
                return TimeSpan.FromMinutes(minutes);
            }

            Log.Warning("Invalid --soak value '{Value}'; using {Default} minutes", value, DefaultMinutes);
            return TimeSpan.FromMinutes(DefaultMinutes);
        }
        return null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // WPF can deadlock while it destroys a window that holds the mouse capture (the stylus code waits for its pen
        // thread, already gone). The process then lingers without a window and keeps the install folder locked, so an
        // update can never replace it. Release the capture first, and force the exit if shutdown still hangs.
        Mouse.Capture(null);
        int exitCode = e.ApplicationExitCode;
        new Thread(() => { Thread.Sleep(TimeSpan.FromSeconds(5)); Environment.Exit(exitCode); })
        { IsBackground = true, Name = "Island.ExitWatchdog" }.Start();

        foreach (var source in _systemNoticeSources)
        {
            source.NoticeRaised -= OnSystemNoticeRaised;
            source.Dispose();
        }
        _noticeCoordinator = null;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _demoTimer?.Stop();
        _soak?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _settingsWindow?.CloseForShutdown();
        _window?.Close();
        _services?.Dispose();
        _guard?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
