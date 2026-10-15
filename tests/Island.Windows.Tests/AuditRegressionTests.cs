using Island.App.ViewModels;
using Island.Core.Budgets;
using Island.Core.Configuration;
using Island.Windows.Budgets;
using Island.Windows.Configuration;

namespace Island.Windows.Tests;

public sealed class AuditRegressionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "island-audit-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void Reset_preserves_layout_position_pomodoro_and_update_source()
    {
        var original = new IslandSettings { ShelfWidgets = ["calculator"], ShelfRows = [1], Dock = DockEdge.Right,
            PositionY = 321, PomodoroFocusMinutes = 45, PomodoroCycles = 6,
            UpdateRepository = "owner/custom", PhoneFcmToken = "credential", PhoneBlockEnabled = true, ShowVolume = false };
        IslandSettings? saved = null;
        var vm = new SettingsViewModel(original, ["Primary"], s => saved = s);
        vm.ResetToDefaultsCommand.Execute(null);
        Assert.NotNull(saved);
        Assert.Equal(original.ShelfWidgets, saved.ShelfWidgets);
        Assert.Equal(original.ShelfRows, saved.ShelfRows);
        Assert.Equal(original.Dock, saved.Dock);
        Assert.Equal(original.PositionY, saved.PositionY);
        Assert.Equal(45, saved.PomodoroFocusMinutes);
        Assert.Equal(6, saved.PomodoroCycles);
        Assert.Equal(original.UpdateRepository, saved.UpdateRepository);
        Assert.Equal("credential", saved.PhoneFcmToken);
        Assert.False(saved.PhoneBlockEnabled);
        Assert.True(saved.ShowVolume);
    }

    [Fact]
    public void Corrupt_budget_file_is_preserved_before_new_changes()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "budgets.json");
        const string corrupt = "{broken budget data";
        File.WriteAllText(path, corrupt);
        var store = new JsonBudgetStore(_dir);
        var book = new BudgetBook(store);
        book.Add();
        string backup = Assert.Single(Directory.GetFiles(_dir, "budgets.json.bad*"));
        Assert.Equal(corrupt, File.ReadAllText(backup));
        Assert.Equal(2, new JsonBudgetStore(_dir).Load().Budgets.Count);
    }

    [Fact]
    public void Failed_budget_read_disables_writes_even_after_lock_is_released()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "budgets.json");
        var store = new JsonBudgetStore(_dir);
        var original = new BudgetsData([new Budget { Name = "Keep me" }], null);
        store.Save(original);
        string before = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            store.Load();
        Assert.Throws<IOException>(() => store.Save(new BudgetsData([], null)));
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void Settings_normalize_notice_pomodoro_and_clipboard_limits()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            """{"noticeSeconds":-5,"pomodoroCycles":-2,"pomodoroFocusMinutes":0,"pomodoroBreakMinutes":999,"clipboardMaxItems":0}""");
        var s = new JsonSettingsStore(_dir).Load();
        Assert.True(s.NoticeSeconds > 0);
        Assert.Equal(1, s.PomodoroCycles);
        Assert.Equal(1, s.PomodoroFocusMinutes);
        Assert.Equal(120, s.PomodoroBreakMinutes);
        Assert.Equal(1, s.ClipboardMaxItems);
    }

    [Fact]
    public void Settings_store_protects_phone_token_on_disk_and_reads_it_back()
    {
        var store = new JsonSettingsStore(_dir);
        store.Save(new IslandSettings { PhoneFcmToken = "private-phone-registration" });
        string json = File.ReadAllText(Path.Combine(_dir, "settings.json"));
        Assert.DoesNotContain("private-phone-registration", json);
        Assert.Equal("private-phone-registration", new JsonSettingsStore(_dir).Load().PhoneFcmToken);
    }

    [Fact]
    public void Settings_edits_and_reset_use_latest_external_state()
    {
        var current = new IslandSettings();
        var vm = new SettingsViewModel(current, ["Primary"], next => current = next, getCurrent: () => current);
        current = current with { ShelfWidgets = ["calculator"], PomodoroCycles = 8, PhoneFcmToken = "new-token" };
        vm.ShowMedia = false;
        Assert.Equal(["calculator"], current.ShelfWidgets);
        Assert.Equal(8, current.PomodoroCycles);
        vm.ResetToDefaultsCommand.Execute(null);
        Assert.Equal(["calculator"], current.ShelfWidgets);
        Assert.Equal("new-token", current.PhoneFcmToken);
    }

    [Fact]
    public void Unreadable_counter_is_never_overwritten_by_a_default_session()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "counter.json");
        File.WriteAllText(path, """{"value":45,"step":1,"goal":0,"label":"keep"}""");
        string before = File.ReadAllText(path);
        using var store = new Island.Windows.Tools.Counter.CounterStore(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) store.Load();
        store.Save(Island.Core.Tools.Counter.CounterState.Default);
        store.Flush();
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void An_unreadable_DPAPI_token_can_be_explicitly_replaced_without_losing_preferences()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{"phoneFcmToken":"dpapi:v1:invalid","pomodoroCycles":6,"shelfWidgets":["calculator"]}""");
        var store = new JsonSettingsStore(_dir);
        var loaded = store.Load();
        Assert.Equal(string.Empty, loaded.PhoneFcmToken);
        Assert.Throws<IOException>(() => store.Save(loaded with { ShowMedia = false }));
        store.Save(loaded with { PhoneFcmToken = "replacement-token" });
        var restored = new JsonSettingsStore(_dir).Load();
        Assert.Equal("replacement-token", restored.PhoneFcmToken);
        Assert.Equal(6, restored.PomodoroCycles);
        Assert.Equal(["calculator"], restored.ShelfWidgets);
    }

    [Theory]
    [InlineData("budgets.json", "{}")]
    [InlineData("budgets.json", "{\"budgets\":null}")]
    [InlineData("counter.json", "null")]
    [InlineData("counter.json", "{}")]
    [InlineData("schedule.json", "null")]
    [InlineData("schedule.json", "{}")]
    [InlineData("schedule.json", "{\"starts\":null}")]
    public void Invalid_store_structure_is_preserved_before_recovery(string name, string json)
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, json);
        if (name == "budgets.json") new JsonBudgetStore(_dir).Load();
        else if (name == "counter.json")
        {
            using var store = new Island.Windows.Tools.Counter.CounterStore(path);
            store.Load();
        }
        else new Island.Windows.Pomodoro.JsonPomodoroScheduleStore(path).Load();
        string backup = Assert.Single(Directory.GetFiles(_dir, name + ".bad*"));
        Assert.Equal(json, File.ReadAllText(backup));
    }
}
