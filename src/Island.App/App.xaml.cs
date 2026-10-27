using System.Globalization;
using System.IO;
using Island.App.Audio;
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
using Island.Core.Progress;
using Island.Windows.Focus;
using Island.Windows.Input;
using Island.Windows.Pomodoro;
using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.GameNotes;
using Island.Core.Models;
using Island.Core.Notes;
using Island.Core.Performance;
using Island.Windows.Display;
using Island.Windows.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Serilog;
using MessageBox = System.Windows.MessageBox;

namespace Island.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? _guard;
    private ServiceProvider? _services;
    private IslandWindow? _window;
    private PomodoroTransitionWindow? _pomodoroTransitionWindow;
    private SettingsWindow? _settingsWindow;
    private TrayIconService? _tray;
    private DispatcherTimer? _demoTimer;
    private GlobalHotkey? _hotkey;
    private QuickNotesHotkeyController? _quickNotesHotkey;
    private QuickNotesWindowManager? _quickNotesWindowManager;
    private bool _quitRequestInProgress;
    private AngryPomodoro? _angry;
    private SoakRunner? _soak;
    private ISystemNoticeSource[] _systemNoticeSources = [];
    private IslandCoordinator? _noticeCoordinator;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --smoke-test (used by release.ps1): run the real startup with fake services next to a running instance, then
        // exit 0 after a few seconds, or 1 on the first unhandled exception. Catches an app that does not start before it is published.
        bool smoke = e.Args.Contains("--smoke-test");
        if (!smoke)
        {
            _guard = SingleInstanceGuard.TryAcquire("DynamicIsland");
            if (_guard is null) { Shutdown(); return; }
        }

        // Software rendering: the island is a small layered window, and the GPU path costs far more than it saves.
        // It loads the display driver (about 60 MB private memory and 1,200 handles here) and reads every frame back
        // from the GPU, which used more CPU than rasterizing the pill directly. Must be set before the first window.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "logs");
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(Path.Combine(logDir, "island-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 5)
            .CreateLogger();
        var loggerFactory = new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger);

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error(ex.Exception, "Unhandled UI exception");
            ex.Handled = true;
            if (smoke) Shutdown(1);
        };

        TimeSpan? soak = ParseSoak(e.Args);
        bool demo = e.Args.Contains("--demo") || soak is not null || smoke;
        _services = ServiceRegistration.Build(demo, loggerFactory);
        var sp = _services;

        var quickNotes = sp.GetRequiredService<QuickNotesService>();
        try { await quickNotes.InitializeAsync(); }
        catch (Exception ex) { Log.Error(ex, "Quick notes failed to initialize"); }

        // GameNotes: the foreground game is tracked from launch, so its notes are ready when the widget or Ctrl+Alt+G opens them.
        sp.GetRequiredService<IForegroundGameTracker>().Start();
        sp.GetRequiredService<GameNotesHotkeyController>().Start();
        try { await sp.GetRequiredService<GameNotesService>().InitializeAsync(); }
        catch (Exception ex) { Log.Error(ex, "Game notes failed to initialize"); }
        _quickNotesWindowManager = sp.GetRequiredService<QuickNotesWindowManager>();
        _quickNotesHotkey = new QuickNotesHotkeyController(
            () => new GlobalHotkey(GlobalHotkey.ModControl | GlobalHotkey.ModAlt, 0x4E /* N */),
            _quickNotesWindowManager.OpenForCapture,
            action => Dispatcher.BeginInvoke(action),
            conflict =>
            {
                _quickNotesWindowManager.SetHotkeyConflict(conflict);
                if (conflict) Log.Warning("Ctrl+Alt+N is already taken; quick-notes hotkey is unavailable");
            });

        var holder = sp.GetRequiredService<SettingsHolder>();
        var monitors = sp.GetRequiredService<MonitorService>();
        var media = sp.GetRequiredService<IMediaService>();
        var volume = sp.GetRequiredService<IVolumeService>();
        var display = sp.GetRequiredService<IDisplayService>();
        var coordinator = sp.GetRequiredService<IslandCoordinator>();

        var applier = sp.GetRequiredService<SettingsApplier>();
        var vm = sp.GetRequiredService<IslandViewModel>();
        var updates = sp.GetRequiredService<IUpdateService>();
        var angryResume = sp.GetRequiredService<JsonAngryResumeStore>();
        _window = new IslandWindow(vm, monitors, sp.GetRequiredService<Func<IslandSettings>>(), applier.Apply, () => updates.Available);
        applier.Applied = () => Dispatcher.BeginInvoke(() =>
        {
            _window?.ApplySettings();
            _quickNotesHotkey?.SetEnabled(holder.Current.QuickNotesHotkeyEnabled);
        });
        _quickNotesHotkey.SetEnabled(holder.Current.QuickNotesHotkeyEnabled);
        _window.OpenSettingsRequested += () => OpenSettings(holder, applier, monitors);
        _window.QuitRequested += RequestQuit;
        _window.InstallUpdateRequested += () => _ = InstallUpdateAsync(updates, coordinator, angryResume);
        _window.CheckUpdateRequested += () => _ = CheckUpdatesFromMenuAsync(updates, coordinator);
        _window.TaskbarRecreated += () => _tray?.Refresh();
        _window.Show();

        volume.Initialize();
        display.Start();
        try { await media.InitializeAsync(); }
        catch (Exception ex) { Log.Error(ex, "Media service failed to initialize"); }
        // The Mini pill is remembered: keep the saved flag in step with the island state (written only when it changes).
        coordinator.SubscriberFailed += ex => Log.Error(ex, "Island state subscriber failed");
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

        // Performance: temperature alerts are normal notices; sampling starts here (alerts on, or the widget on screen).
        PerformanceWiring.Attach(sp.GetRequiredService<PerformanceMonitor>(), coordinator);

        // Progress of the Suite: running tasks show on the capsule and in the Tasks widget; a finished or failed one raises a notice.
        ProgressWiring.Attach(sp.GetRequiredService<ProgressTracker>(), coordinator);

        // Angry pomodoro: while locked, browsers on distracting sites are minimized; a blocked site raises a notice.
        var angry = sp.GetRequiredService<AngryPomodoro>();
        var siteGuard = sp.GetRequiredService<ForegroundSiteGuard>();
        _angry = angry;
        // A locked focus blocks every site in the list. The free break, and any session time without a lock, blocks only adult sites.
        angry.LockChanged += () => Dispatcher.BeginInvoke(() => siteGuard.SetMode(
            angry.IsLocked ? SiteGuardMode.All : angry.IsSessionActive ? SiteGuardMode.AdultOnly : SiteGuardMode.Off));
        // The scheduled start is resolved here so it exists (and is disposed with the container) from launch.
        sp.GetRequiredService<PomodoroSchedule>();
        // The chosen Angry desktop (saved in settings) is held while a focus is locked; see AngryDesktopLock.
        sp.GetRequiredService<AngryDesktopLock>().SetTargetDesktop(AngryDesktopLock.ParseDesktopId(holder.Current.PomodoroAngryDesktopId));
        siteGuard.SiteBlocked += site => coordinator.Post(new IslandEvent.NoticeRaised(
            new Notice($"{site} bloqueado", "Pomodoro raivoso ativo", "timer")));

        // Each locked focus of an angry plan -> ask the phone to block for the time left of that focus. Breaks are free,
        // so the full block is released when it expires. The same message carries an adult-only span (adultEndMs) that
        // runs through this focus and its break when the plan has another focus after it. That next focus sends its own
        // block when it locks, so the adult block is extended focus by focus. The last focus of a plan sends no adult
        // span: the full block already covers it. Adult content is never free during an angry session, breaks included.
        // Leaving Angry early sends nothing: the phone keeps its block until the original end (or until the phrase is
        // typed on the phone). LockChanged also fires for session changes that are not a new lock, so only a false -> true
        // edge of IsLocked sends; the lock guards the edge state.
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
            _ = BlockPhoneAsync(phone, coordinator, focusTimer.Remaining, AdultSpanFromFocus(focusTimer),
                holder.Current.PhoneFcmToken);
        };

        // An Angry session that was running when an update restarted the app continues from its saved time. It is restored
        // after the handlers above are wired (so the lock, phone block and desktop lock follow it) and before the weekly
        // routine checks the clock, which then does not start a second session.
        if (!smoke && angryResume.Take(DateTimeOffset.Now, AngryResumeMaxAge) is { } resume)
            angry.RestoreSession(resume);

        // Pomodoro -> toast (and sound when enabled), clipboard capture -> history, global hotkey -> clipboard panel
        var pomodoro = sp.GetRequiredService<PomodoroTimer>();
        pomodoro.Transitioned += t =>
        {
            coordinator.Post(new IslandEvent.NoticeRaised(PomodoroNotices.For(t)));
            if (PomodoroSoundSelection.For(t, holder.Current) is { } sound)
                sp.GetRequiredService<PomodoroSoundPlayer>().Play(sound);
            Dispatcher.BeginInvoke(() =>
            {
                _pomodoroTransitionWindow?.Close();
                if (t.PlanFinished || _window is not { IsLoaded: true }) return;
                var interlude = new PomodoroTransitionWindow(t, holder.Current.ReduceAnimations, pomodoro.SessionGoal,
                    t.AutoStarted ? null : () =>
                    {
                        if (pomodoro.IsAwaitingNextPhase && pomodoro.Phase == t.Next) pomodoro.Start();
                    });
                _pomodoroTransitionWindow = interlude;
                interlude.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_pomodoroTransitionWindow, interlude)) _pomodoroTransitionWindow = null;
                };
                interlude.ShowOnIslandMonitor(_window);
            });
        };
        pomodoro.BreakApproaching += () => Dispatcher.BeginInvoke(() =>
        {
            if (!pomodoro.IsPreparingForBreak) return;
            if (coordinator.State.Mode == IslandMode.Expanded && holder.Current.ShelfWidgets.Contains("pomodoro")) return;
            coordinator.Post(new IslandEvent.NoticeRaised(new Notice("Pausa em 30 segundos", "Conclua seu raciocínio; a pausa está chegando.", "timer", Urgent: true)));
        });
        var weeklyPomodoroSchedule = sp.GetRequiredService<PomodoroWeeklySchedule>();
        if (!smoke)
        {
            bool resumePromptOpen = false;
            void ShowResumePrompt()
            {
                if (resumePromptOpen) return;
                resumePromptOpen = true;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (!weeklyPomodoroSchedule.HasPendingResumePrompt) return;
                        MessageBoxResult answer = MessageBox.Show(_window,
                            "A rotina semanal ficou pausada por um dia. Deseja reativá-la para hoje?",
                            "Rotina do Pomodoro", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        weeklyPomodoroSchedule.AnswerResumePrompt(answer == MessageBoxResult.Yes);
                    }
                    finally { resumePromptOpen = false; }
                }), DispatcherPriority.ApplicationIdle);
            }
            weeklyPomodoroSchedule.ResumeConfirmationRequested += ShowResumePrompt;
            if (weeklyPomodoroSchedule.HasPendingResumePrompt) ShowResumePrompt();
        }
        if (!smoke) weeklyPomodoroSchedule.RefreshNow();
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
        // Capture shortcuts (Ctrl+Alt+P / Ctrl+Alt+R). Not registered in demo mode, so a demo run cannot take the keys.
        if (!demo) sp.GetRequiredService<CaptureHotkeys>().Start();
        if (demo && soak is null && !smoke) StartDemo(sp);

        _tray = new TrayIconService(
            openSettings: () => OpenSettings(holder, applier, monitors),
            setPaused: paused => coordinator.Post(new IslandEvent.PausedChanged(paused)),
            exit: RequestQuit);

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnSystemTimeChanged;

        // Soak test (--soak): fake events for the given duration, then shut down. Its verdict is in the log.
        if (soak is { } soakDuration)
        {
            _soak = new SoakRunner(coordinator, sp.GetRequiredService<FakeMediaService>(),
                sp.GetRequiredService<FakeVolumeService>(), Dispatcher, soakDuration,
                () => Dispatcher.BeginInvoke(new Action(ShutdownAfterQuickNotesFlush)));
            _soak.Start();
        }

        if (smoke)
        {
            // Runs the post-resume surface refresh (Nudge, then a re-show 1.5 s later) so the published build exercises it.
            var nudge = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.5) };
            nudge.Tick += (_, _) => { nudge.Stop(); _window?.Nudge(); };
            nudge.Start();

            var done = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            done.Tick += (_, _) => { done.Stop(); Log.Information("Smoke test passed"); Shutdown(0); };
            done.Start();
        }
    }

    /// <summary>Routes passive Windows signals through the coordinator's shared priorities and timer.</summary>
    private void OnSystemNoticeRaised(object? sender, Notice notice) =>
        _noticeCoordinator?.Post(new IslandEvent.NoticeRaised(notice));

    /// <summary>Fire-and-forget: the notifier never throws, and a failure only raises a toast. Angry keeps running either way.</summary>
    private static async Task BlockPhoneAsync(IPhoneBlockNotifier phone, IslandCoordinator coordinator, TimeSpan remaining,
        TimeSpan adultRemaining, string token)
    {
        PhoneBlockOutcome outcome = await phone.NotifyAsync(remaining, token, adultRemaining).ConfigureAwait(false);
        coordinator.Post(new IslandEvent.NoticeRaised(outcome.Delivered
            ? new Notice("Celular bloqueado", outcome.Detail, "timer")
            : new Notice("Celular não bloqueado", outcome.Detail, "timer")));
    }

    /// <summary>
    /// How long the phone keeps adult content blocked from now: the focus that is starting, then its break. The break is
    /// the one the plan captured when it started (a weekly block has its own), not the current setting. A plan ends
    /// after its last break, so the last focus counts its break too. A single focus (no plan) gives zero, because the
    /// session ends with it and the full block already covers it.
    /// </summary>
    private static TimeSpan AdultSpanFromFocus(PomodoroTimer timer)
    {
        if (!timer.PlanActive) return TimeSpan.Zero;
        return timer.Remaining + timer.PlanBreakDuration;
    }

    /// <summary>Quit from the island menu or the tray. While an angry pomodoro is locked, the unlock dialog must release it first.</summary>
    private async void RequestQuit()
    {
        if (_quitRequestInProgress) return;
        _quitRequestInProgress = true;
        try
        {
            if (!ConfirmNotLocked()) return;
            if (!await FlushQuickNotesBeforeShutdownAsync()) return;

            Shutdown();
        }
        finally
        {
            _quitRequestInProgress = false;
        }
    }

    /// <summary>
    /// "Install update" from the island menu. An Angry session does not block it: the session is saved and the new process
    /// continues it (see <see cref="PrepareForUpdate"/>). Progress is shown as temporary notices; failures are logged and shown as a notice.
    /// </summary>
    private async Task InstallUpdateAsync(IUpdateService updates, IslandCoordinator coordinator, JsonAngryResumeStore angryResume)
    {
        if (_quitRequestInProgress) return;
        UpdateInfo? update = updates.Available;
        if (update is null) return;
        _quitRequestInProgress = true;
        bool launched = false;
        try
        {
            coordinator.Post(new IslandEvent.NoticeRaised(new Notice($"Downloading {update.Tag}", "Update", "timer")));
            launched = await updates.InstallAsync(update, beforeLaunch: () => Dispatcher.InvokeAsync(async () =>
            {
                // No edits or new note windows may slip between this flush and process exit.
                _quickNotesWindowManager?.SetEditingEnabled(false);
                return await FlushQuickNotesBeforeShutdownAsync(reopenOnFailure: false) && PrepareForUpdate(angryResume);
            }).Task.Unwrap());
            if (launched) Shutdown(); // No awaits or fallible notifications after the helper has started.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Installing update {Tag} failed", update.Tag);
            if (!launched) coordinator.Post(new IslandEvent.NoticeRaised(new Notice("Update failed", "See the logs", "timer")));
        }
        finally
        {
            if (!launched)
            {
                _quickNotesWindowManager?.SetEditingEnabled(true);
                if (_quickNotesWindowManager?.HasSaveError == true) _quickNotesWindowManager.OpenNotes();
            }
            _quitRequestInProgress = false;
        }
    }

    // An Angry session saved before an update is taken again only this soon after, so an update that never relaunched cannot resume it later.
    private static readonly TimeSpan AngryResumeMaxAge = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The last check before an update replaces the app. An Angry session is saved for the new process to continue, so
    /// no phrase is asked. When it cannot be saved, the phrase is still required.
    /// </summary>
    private bool PrepareForUpdate(JsonAngryResumeStore angryResume)
    {
        if (_angry?.CaptureSession() is not { } session) return ConfirmNotLocked();
        return angryResume.TrySave(session, DateTimeOffset.Now) || ConfirmNotLocked();
    }

    private async Task<bool> FlushQuickNotesBeforeShutdownAsync(bool reopenOnFailure = true)
    {
        if (_quickNotesWindowManager is not { } notes) return true;

        await notes.FlushPendingSaveAsync();
        if (!notes.HasSaveError) return true;

        if (reopenOnFailure) notes.OpenNotes();
        return false;
    }

    private async void ShutdownAfterQuickNotesFlush()
    {
        try
        {
            if (await FlushQuickNotesBeforeShutdownAsync()) Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not flush quick notes before soak shutdown");
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
                () => CheckForUpdatesAsync(updates), () => holder.Current,
                _services!.GetRequiredService<PomodoroSoundPlayer>().Play);
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
        if (e.Mode != PowerModes.Resume) return;
        Dispatcher.BeginInvoke(() => _window?.Nudge());
        Dispatcher.BeginInvoke(new Action(() => _services?.GetService<PomodoroWeeklySchedule>()?.RefreshNow()));
    }

    private void OnSystemTimeChanged(object? s, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => _services?.GetService<PomodoroWeeklySchedule>()?.RefreshNow()));

    /// <summary>Demo: a looping fake playlist plus periodic volume changes, so every state can be seen without real media.</summary>
    private void StartDemo(IServiceProvider sp)
    {
        var media = sp.GetRequiredService<FakeMediaService>();
        var vol = sp.GetRequiredService<FakeVolumeService>();
        var progress = sp.GetRequiredService<FakeProgressFeed>();
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
            // Demo task: climbs from 0 to 100 in about 12 s, then finishes. A new id every 30 s cycle.
            int step = n % 30;
            string taskId = $"demo-{n / 30}";
            if (step == 1) progress.Push(new ProgressUpdate(taskId, "Cortar: aula.mp4", "Corte", ProgressState.Running, 0.0, null, "SuperPrograma"));
            else if (step is >= 2 and <= 13) progress.Push(new ProgressUpdate(taskId, "Cortar: aula.mp4", "Corte", ProgressState.Running, (step - 1) * 100.0 / 12.0, null, "SuperPrograma"));
            else if (step == 14) progress.Push(new ProgressUpdate(taskId, "Cortar: aula.mp4", "Corte", ProgressState.Done, 100.0, "Corte concluído", "SuperPrograma"));
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
        SystemEvents.TimeChanged -= OnSystemTimeChanged;
        _demoTimer?.Stop();
        _soak?.Dispose();
        _hotkey?.Dispose();
        _quickNotesHotkey?.Dispose();
        _tray?.Dispose();
        _quickNotesWindowManager?.CloseForShutdown();
        _settingsWindow?.CloseForShutdown();
        _pomodoroTransitionWindow?.Close();
        _window?.Close();
        _services?.Dispose();
        _guard?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
