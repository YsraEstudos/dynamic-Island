using Island.Core.Tools.Counter;
using Island.Windows.Tools.Counter;

namespace Island.Core.Tests.Tools.Counter;

public sealed class CounterStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "island-counter-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "counter.json");

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Round_trip_preserves_value_step_goal_and_label()
    {
        var state = new CounterState(-12, 5, 50, "Café");
        using (var store = new CounterStore(FilePath))
        {
            store.Save(state);
            store.Flush();
        }

        CounterState loaded = new CounterStore(FilePath).Load();

        Assert.Equal(state, loaded);
    }

    [Fact]
    public void Missing_file_loads_the_default_state()
    {
        CounterState loaded = new CounterStore(FilePath).Load();

        Assert.Equal(CounterState.Default, loaded);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Corrupt_file_loads_the_default_state_without_throwing()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{ invalid");

        CounterState loaded = new CounterStore(FilePath).Load();

        Assert.Equal(CounterState.Default, loaded);
    }

    [Fact]
    public void Wrong_field_type_loads_the_default_state_without_throwing()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{ \"value\": \"many\", \"step\": 1, \"goal\": 0, \"label\": \"x\" }");

        CounterState loaded = new CounterStore(FilePath).Load();

        Assert.Equal(CounterState.Default, loaded);
    }

    [Fact]
    public void Save_creates_the_missing_folder()
    {
        using var store = new CounterStore(FilePath);

        store.Save(new CounterState(3, 1, 0, "Contador"));
        store.Flush();

        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void A_burst_of_saves_writes_only_the_latest_state()
    {
        using (var store = new CounterStore(FilePath))
        {
            store.Save(new CounterState(1, 1, 0, "Contador"));
            store.Save(new CounterState(2, 1, 0, "Contador"));
            store.Save(new CounterState(3, 1, 0, "Contador"));
            store.Flush();
        }

        Assert.Equal(3, new CounterStore(FilePath).Load().Value);
    }

    [Fact]
    public void Disposing_writes_a_pending_save()
    {
        var store = new CounterStore(FilePath);
        store.Save(new CounterState(8, 1, 0, "Contador"));

        store.Dispose();

        Assert.Equal(8, new CounterStore(FilePath).Load().Value);
    }

    [Fact]
    public void Save_after_dispose_is_ignored()
    {
        var store = new CounterStore(FilePath);
        store.Dispose();

        store.Save(new CounterState(5, 1, 0, "Contador"));
        store.Flush();

        Assert.False(File.Exists(FilePath));
    }
    [Fact]
    public void Changes_are_saved_through_the_store_when_the_model_is_disposed()
    {
        string directory = Path.Combine(Path.GetTempPath(), "island-counter-model-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "counter.json");
        try
        {
            var model = new CounterModel(CounterState.Default, new CounterStore(path));

            model.Increment();
            model.CycleLabel();
            model.Dispose();

            CounterState saved = new CounterStore(path).Load();
            Assert.Equal(1, saved.Value);
            Assert.Equal("Água", saved.Label);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}