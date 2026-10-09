using System.Globalization;
using System.Text.Json;
using Island.Core.Notes;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Notes;

public sealed class JsonQuickNotesStore : IQuickNotesStore
{
    private const int CurrentSchemaVersion = 1;
    private const string FileName = "notes.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _directory;
    private readonly string _path;
    private readonly string _tempPath;
    private readonly ILogger<JsonQuickNotesStore>? _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonQuickNotesStore(string? directory = null, ILogger<JsonQuickNotesStore>? log = null)
    {
        _directory = directory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland");
        _path = Path.Combine(_directory, FileName);
        _tempPath = _path + ".tmp";
        _log = log;
    }

    public async Task<IReadOnlyList<QuickNote>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return Array.Empty<QuickNote>();

            try
            {
                await using var stream = new FileStream(
                    _path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                var schemaVersion = ReadSchemaVersion(document.RootElement);
                if (schemaVersion > CurrentSchemaVersion || schemaVersion < CurrentSchemaVersion)
                    throw new UnsupportedNotesSchemaException(schemaVersion);

                stream.Position = 0;
                var notesFile = await JsonSerializer.DeserializeAsync<NotesFile>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                if (notesFile?.Notes is null)
                    throw new JsonException("The notes collection is missing.");

                ValidateNotes(notesFile.Notes);
                return Array.AsReadOnly(notesFile.Notes.ToArray());
            }
            catch (UnsupportedNotesSchemaException)
            {
                throw;
            }
            catch (JsonException exception)
            {
                var recoveryPath = Quarantine();
                _log?.LogWarning(exception, "Invalid quick notes JSON was moved to {RecoveryPath}.", recoveryPath);
                return Array.Empty<QuickNote>();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(IReadOnlyList<QuickNote> notes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notes);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            var snapshot = new NotesFile
            {
                SchemaVersion = CurrentSchemaVersion,
                Notes = notes.ToList()
            };

            try
            {
                await using (var stream = new FileStream(
                    _tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken)
                        .ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(_path))
                    File.Replace(_tempPath, _path, destinationBackupFileName: null);
                else
                    File.Move(_tempPath, _path);
            }
            finally
            {
                TryDelete(_tempPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private string Quarantine()
    {
        var defaultRecoveryPath = _path + ".bad";
        var recoveryPath = File.Exists(defaultRecoveryPath)
            ? _path + ".bad-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N")
            : defaultRecoveryPath;

        File.Move(_path, recoveryPath);
        return recoveryPath;
    }

    private static int ReadSchemaVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("schemaVersion", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var schemaVersion))
        {
            throw new JsonException("The notes schema version is missing or invalid.");
        }

        return schemaVersion;
    }

    private static void ValidateNotes(IEnumerable<QuickNote?> notes)
    {
        foreach (var note in notes)
        {
            if (note is null
                || note.Title is null
                || note.Content is null
                || note.Checklist is null
                || note.Tags is null
                || !Enum.IsDefined(note.Color))
            {
                throw new JsonException("A quick note contains invalid or missing fields.");
            }

            if (note.Checklist.Any(item => item is null || item.Text is null)
                || note.Tags.Any(tag => tag is null))
            {
                throw new JsonException("A quick note contains an invalid checklist item or tag.");
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort; a later save will replace the temporary file.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort; a later save will replace the temporary file.
        }
    }

    private sealed class NotesFile
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public List<QuickNote> Notes { get; set; } = [];
    }
}

public sealed class UnsupportedNotesSchemaException : Exception
{
    public UnsupportedNotesSchemaException(int schemaVersion)
        : base($"Quick notes schema version {schemaVersion} is not supported.")
    {
        SchemaVersion = schemaVersion;
    }

    public int SchemaVersion { get; }
}
