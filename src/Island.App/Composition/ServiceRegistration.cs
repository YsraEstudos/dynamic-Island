using Island.App.ViewModels;
using Island.App.Widgets;
using Island.App.Shell;
using Island.Core.Clipboard;
using Island.Core.Calendar;
using Island.Core.Budgets;
using Island.Core.GameNotes;
using Island.Core.Notes;
using Island.Core.Pomodoro;
using Island.Core.Shelf;
using Island.Windows.Clipboard;
using Island.Core.Abstractions;
using Island.Core.Application;
using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Windows.Audio;
using Island.Windows.Configuration;
using Island.Windows.Calendar;
using Island.Windows.Budgets;
using Island.Windows.Notes;
using Island.Windows.GameNotes;
using Island.Windows.Display;
using Island.Windows.Devices;
using Island.Windows.Focus;
using Island.Windows.Input;
using Island.Windows.Media;
using Island.Windows.Phone;
using Island.Windows.Updates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Island.App.Composition;

/// <summary>Holds the live settings; everything reads through <see cref="Current"/> so changes apply immediately.</summary>
public sealed class SettingsHolder(IslandSettings initial)
{
    public IslandSettings Current { get; set; } = initial;
}

/// <summary>Single place that updates live settings, persists them and tells the window to re-apply.</summary>
public sealed class SettingsApplier(SettingsHolder holder, ISettingsStore store, ILogger<SettingsApplier> log)
{
    public Action? Applied { get; set; }

    public void Apply(IslandSettings s)
    {
        bool startupChanged = holder.Current.StartWithWindows != s.StartWithWindows;
        holder.Current = s;
        try { store.Save(s); } catch (Exception ex) { log.LogError(ex, "Saving settings failed"); }
        if (startupChanged)
        {
            try { Island.Windows.Shell.StartupRegistration.Set(s.StartWithWindows); }
            catch (Exception ex) { log.LogWarning(ex, "Startup registration failed"); }
        }
        Applied?.Invoke();
    }
}

public static class ServiceRegistration
{
    public static ServiceProvider Build(bool demo, ILoggerFactory loggerFactory)
    {
        var s = new ServiceCollection();
        Configure(s, demo, loggerFactory);
        return s.BuildServiceProvider();
    }

    /// <summary>Registers every service. Split from <see cref="Build"/> so tests can validate the whole graph (ValidateOnBuild).</summary>
    public static void Configure(IServiceCollection s, bool demo, ILoggerFactory loggerFactory)
    {
        s.AddSingleton(loggerFactory);
        s.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

        s.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore());
        s.AddSingleton<IQuickNotesStore>(sp => new JsonQuickNotesStore(log: sp.GetService<ILogger<JsonQuickNotesStore>>()));
        s.AddSingleton<QuickNotesService>();
        s.AddSingleton(sp => new QuickNotesWindowManager(
            sp.GetRequiredService<QuickNotesService>(), () => sp.GetRequiredService<SettingsHolder>().Current.ReduceAnimations));
        s.AddSingleton<IQuickNotesWindowHost>(sp => sp.GetRequiredService<QuickNotesWindowManager>());
        // GameNotes
        if (demo)
        {
            s.AddSingleton<IGameNotesStore>(_ => new InMemoryGameNotesStore(DemoGameNotes.Seed()));
            s.AddSingleton<IForegroundGameTracker>(_ => new FakeForegroundGameTracker(DemoGameNotes.InitialGame));
        }
        else
        {
            s.AddSingleton<IGameNotesStore>(sp => new JsonGameNotesStore(log: sp.GetService<ILogger<JsonGameNotesStore>>()));
            s.AddSingleton<IForegroundGameTracker>(sp => new WinEventForegroundGameTracker(
                () => sp.GetRequiredService<SettingsHolder>().Current.GameProcesses,
                sp.GetService<ILogger<WinEventForegroundGameTracker>>()));
        }
        s.AddSingleton<GameNotesService>();
        s.AddSingleton(sp => new GameNotesWindowManager(
            sp.GetRequiredService<GameNotesService>(), () => sp.GetRequiredService<SettingsHolder>().Current.ReduceAnimations));
        s.AddSingleton<IGameNotesWindowHost>(sp => sp.GetRequiredService<GameNotesWindowManager>());
        s.AddSingleton<GameNotesHotkeyController>();
        s.AddSingleton<IBudgetStore>(_ => new JsonBudgetStore());
        s.AddSingleton(sp => new BudgetBook(sp.GetRequiredService<IBudgetStore>()));
        s.AddSingleton<BudgetWindowManager>();
        s.AddSingleton<IBudgetWindowHost>(sp => sp.GetRequiredService<BudgetWindowManager>());
        s.AddSingleton<ICalendarStore>(_ => new JsonCalendarStore());
        s.AddSingleton<CalendarAgenda>();
        s.AddSingleton(sp => new SettingsHolder(sp.GetRequiredService<ISettingsStore>().Load()));
        s.AddSingleton<Func<IslandSettings>>(sp => () => sp.GetRequiredService<SettingsHolder>().Current);
        s.AddSingleton<IIslandScheduler, SystemIslandScheduler>();
        s.AddSingleton<MonitorService>();

        if (demo)
        {
            s.AddSingleton<FakeMediaService>();
            s.AddSingleton<FakeVolumeService>();
            s.AddSingleton<FakeDisplayService>();
            s.AddSingleton<IMediaService>(sp => sp.GetRequiredService<FakeMediaService>());
            s.AddSingleton<IVolumeService>(sp => sp.GetRequiredService<FakeVolumeService>());
            s.AddSingleton<IDisplayService>(sp => sp.GetRequiredService<FakeDisplayService>());
        }
        else
        {
            s.AddSingleton<ISystemNoticeSource, WindowsCapsLockNoticeSource>();
            s.AddSingleton<ISystemNoticeSource, WindowsDeviceNoticeSource>();
            s.AddSingleton<IMediaService, WindowsMediaService>();
            s.AddSingleton<IVolumeService, WindowsVolumeService>();
            s.AddSingleton<IDisplayService>(sp => new FullscreenDetector(
                sp.GetService<ILogger<FullscreenDetector>>(),
                () => sp.GetRequiredService<SettingsHolder>().Current.GameProcesses));
        }

        if (demo) s.AddSingleton<IPhoneBlockNotifier, FakePhoneBlockNotifier>();
        else s.AddSingleton<IPhoneBlockNotifier>(sp => new FcmPhoneBlockNotifier(
            FcmPhoneBlockNotifier.CreateHttpClient(), log: sp.GetService<ILogger<FcmPhoneBlockNotifier>>()));

        s.AddSingleton<SettingsApplier>();
        s.AddSingleton<PomodoroTimer>();
        s.AddSingleton<AngryPomodoro>();
        s.AddSingleton(sp => new PomodoroSchedule(
            sp.GetRequiredService<PomodoroTimer>(), sp.GetRequiredService<AngryPomodoro>(), sp.GetRequiredService<IIslandScheduler>()));
        s.AddSingleton(sp => new ForegroundSiteGuard(sp.GetService<ILogger<ForegroundSiteGuard>>()));
        s.AddSingleton<ClipboardHistory>();
        s.AddSingleton<FileTray>();
        if (demo) s.AddSingleton<IClipboardService, FakeClipboardService>();
        else s.AddSingleton<IClipboardService, WindowsClipboardService>();
        s.AddSingleton(sp => new ShelfContext(
            sp.GetRequiredService<PomodoroTimer>(), sp.GetRequiredService<AngryPomodoro>(), sp.GetRequiredService<PomodoroSchedule>(),
            sp.GetRequiredService<FileTray>(),
            sp.GetRequiredService<ClipboardHistory>(), sp.GetRequiredService<IClipboardService>(),
            sp.GetRequiredService<IMediaService>(), sp.GetRequiredService<CalendarAgenda>(),
            sp.GetRequiredService<QuickNotesService>(), sp.GetRequiredService<IQuickNotesWindowHost>(),
            sp.GetRequiredService<Func<IslandSettings>>(),
            sp.GetRequiredService<SettingsApplier>().Apply,
            sp.GetRequiredService<BudgetBook>(), sp.GetRequiredService<IBudgetWindowHost>(),
            sp.GetRequiredService<GameNotesService>(), sp.GetRequiredService<IGameNotesWindowHost>()));
        if (demo) s.AddSingleton<IUpdateService, FakeUpdateService>();
        else s.AddSingleton<IUpdateService>(sp => new GitHubUpdateService(
            GitHubUpdateService.CreateHttpClient(), () => sp.GetRequiredService<SettingsHolder>().Current.UpdateRepository,
            GitHubUpdateService.RunningVersion(typeof(ServiceRegistration).Assembly), log: sp.GetService<ILogger<GitHubUpdateService>>()));
        s.AddSingleton<IslandCoordinator>();
        s.AddSingleton<IslandViewModel>();
    }
}
