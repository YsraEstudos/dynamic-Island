using Island.Core.GameNotes;
using Island.Windows.GameNotes;

namespace Island.Windows.Tests;

public sealed class JsonGameNotesStoreTests : IDisposable
{
    private static readonly DateTimeOffset Stamp = new(2026, 10, 9, 12, 30, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "island-gamenotes-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task Round_trip_keeps_games_notes_and_flags()
    {
        var store = new JsonGameNotesStore(_directory);
        var note = new GameNote(Guid.NewGuid(), "Pegar a fita", Stamp, IsPinned: true, IsCompleted: false);
        var done = new GameNote(Guid.NewGuid(), "Feito", Stamp.AddMinutes(-1), IsPinned: false, IsCompleted: true);
        var game = new GameNotesGame("celeste", "celeste", "Celeste", @"C:\Games\Celeste.exe", Stamp,
            new[] { done, note });

        await store.SaveAsync(new[] { game });
        GameNotesGame loaded = Assert.Single(await store.LoadAsync());

        Assert.Equal("celeste", loaded.Key);
        Assert.Equal("Celeste", loaded.DisplayName);
        Assert.Equal(@"C:\Games\Celeste.exe", loaded.ExePath);
        Assert.Equal(Stamp, loaded.UpdatedAt);
        // Pinned first, then open, then completed.
        Assert.Equal(2, loaded.Notes.Count);
        Assert.Equal(note, loaded.Notes[0]);
        Assert.Equal(done, loaded.Notes[1]);
    }

    [Fact]
    public async Task Missing_file_loads_as_empty()
    {
        var store = new JsonGameNotesStore(_directory);

        Assert.Empty(await store.LoadAsync());
        Assert.False(File.Exists(Path.Combine(_directory, "gamenotes.json")));
    }

    [Fact]
    public async Task Corrupt_file_is_moved_aside_and_loads_as_empty()
    {
        var store = new JsonGameNotesStore(_directory);
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "gamenotes.json");
        await File.WriteAllTextAsync(path, "{ not json");

        Assert.Empty(await store.LoadAsync());

        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(_directory, "gamenotes.json.bad*"));
    }

    [Fact]
    public async Task Record_with_missing_fields_quarantines_the_whole_file()
    {
        var store = new JsonGameNotesStore(_directory);
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "gamenotes.json");
        await File.WriteAllTextAsync(path, """{ "schemaVersion": 1, "games": [ { "key": "celeste", "notes": [] } ] }""");

        Assert.Empty(await store.LoadAsync());

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Unsupported_schema_is_left_untouched_so_newer_data_is_not_lost()
    {
        var store = new JsonGameNotesStore(_directory);
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "gamenotes.json");
        const string future = """{ "schemaVersion": 99, "games": [] }""";
        await File.WriteAllTextAsync(path, future);

        await Assert.ThrowsAsync<UnsupportedGameNotesSchemaException>(() => store.LoadAsync());

        Assert.Equal(future, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Save_replaces_the_file_and_leaves_no_temporary_file()
    {
        var store = new JsonGameNotesStore(_directory);
        var first = new GameNotesGame("a", "a", "A", null, Stamp, new[] { new GameNote(Guid.NewGuid(), "1", Stamp, false, false) });
        var second = new GameNotesGame("b", "b", "B", null, Stamp, new[] { new GameNote(Guid.NewGuid(), "2", Stamp, false, false) });

        await store.SaveAsync(new[] { first });
        await store.SaveAsync(new[] { second });

        GameNotesGame loaded = Assert.Single(await store.LoadAsync());
        Assert.Equal("b", loaded.Key);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }
}
