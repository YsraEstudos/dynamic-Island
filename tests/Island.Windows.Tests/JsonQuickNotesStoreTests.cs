using Island.Core.Notes;
using Island.Windows.Notes;

namespace Island.Windows.Tests;

public sealed class JsonQuickNotesStoreTests : IDisposable
{
    private static readonly byte[] PriorRecovery = [0x10, 0x20, 0x30];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "island-notes-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task Round_trip_preserves_checklist_tags_and_flags()
    {
        var (store, note) = CreateStoreWithNote();

        await store.SaveAsync([note]);
        var loaded = Assert.Single(await store.LoadAsync());

        Assert.Equal(note.Id, loaded.Id);
        Assert.Equal(note.Checklist, loaded.Checklist);
        Assert.Equal(note.Tags, loaded.Tags);
        Assert.Equal(note.IsPinned, loaded.IsPinned);
        Assert.Equal(note.IsArchived, loaded.IsArchived);
        Assert.Equal(note.Color, loaded.Color);
        Assert.Equal(note.CreatedAt, loaded.CreatedAt);
        Assert.Equal(note.UpdatedAt, loaded.UpdatedAt);
        Assert.Equal(note.DeletedAt, loaded.DeletedAt);
    }

    [Fact]
    public async Task Missing_file_returns_empty_collection()
    {
        var store = CreateEmptyStore();

        Assert.Empty(await store.LoadAsync());
        Assert.False(File.Exists(NotesPath));
    }

    [Fact]
    public async Task Corrupt_file_is_quarantined_without_overwriting_prior_recovery()
    {
        var (store, directory, path, badPath) = CreateStoreWithPaths();
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, "{ invalid");
        var corrupt = await File.ReadAllBytesAsync(path);
        await File.WriteAllBytesAsync(badPath, PriorRecovery);

        Assert.Empty(await store.LoadAsync());

        Assert.Equal(PriorRecovery, await File.ReadAllBytesAsync(badPath));
        Assert.Contains(Directory.GetFiles(directory, "notes.json.bad-*"), candidate =>
            File.ReadAllBytes(candidate).SequenceEqual(corrupt));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Unsupported_schema_is_left_untouched()
    {
        var (store, path) = CreateEmptyStoreWithPath();
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(path, """{ "schemaVersion": 99, "notes": [] }""");
        var original = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAsync<UnsupportedNotesSchemaException>(() => store.LoadAsync());

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Save_replaces_target_and_cleans_temporary_file()
    {
        var (store, tempPath) = CreateEmptyStoreWithTempPath();
        var (first, second) = CreateTwoNotes();

        await store.SaveAsync([first]);
        await store.SaveAsync([second]);

        Assert.Equal(second.Id, Assert.Single(await store.LoadAsync()).Id);
        Assert.False(File.Exists(tempPath));
    }

    private string NotesPath => Path.Combine(_directory, "notes.json");

    private (JsonQuickNotesStore Store, QuickNote Note) CreateStoreWithNote()
    {
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var note = new QuickNote(
            Guid.NewGuid(),
            "Planejar viagem",
            "Pesquisar datas",
            [new QuickNoteChecklistItem(Guid.NewGuid(), "Comprar passagem", true, 0)],
            ["pessoal", "viagem"],
            QuickNoteColor.Blue,
            true,
            true,
            now.AddDays(-1),
            now,
            null);
        return (new JsonQuickNotesStore(_directory), note);
    }

    private JsonQuickNotesStore CreateEmptyStore() => new(_directory);

    private (JsonQuickNotesStore Store, string Directory, string Path, string BadPath) CreateStoreWithPaths() =>
        (new JsonQuickNotesStore(_directory), _directory, NotesPath, NotesPath + ".bad");

    private (JsonQuickNotesStore Store, string Path) CreateEmptyStoreWithPath() =>
        (new JsonQuickNotesStore(_directory), NotesPath);

    private (JsonQuickNotesStore Store, string TempPath) CreateEmptyStoreWithTempPath() =>
        (new JsonQuickNotesStore(_directory), NotesPath + ".tmp");

    private (QuickNote First, QuickNote Second) CreateTwoNotes()
    {
        var (_, first) = CreateStoreWithNote();
        var second = first with { Id = Guid.NewGuid(), Title = "Anotar ideia" };
        return (first, second);
    }
}
