using System.Globalization;
using System.Text.Json;
using Island.Core.GameNotes;
using Microsoft.Extensions.Logging;

namespace Island.Windows.GameNotes;

/// <summary>
/// Stores game notes in <c>%LocalAppData%\DynamicIsland\gamenotes.json</c>. Writes go to a temporary file that
/// replaces the target, so a crash never leaves half a file. A corrupt file is moved aside, not overwritten.
/// </summary>
public sealed class JsonGameNotesStore : IGameNotesStore
{
    private const int CurrentSchemaVersion = 1;
    private const string FileName = "gamenotes.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _directory;
    private readonly string _path;
    private readonly string _tempPath;
    private readonly ILogger<JsonGameNotesStore>? _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonGameNotesStore(string? directory = null, ILogger<JsonGameNotesStore>? log = null)
    {
        _directory = directory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland");
        _path = Path.Combine(_directory, FileName);
        _tempPath = _path + ".tmp";
        _log = log;
    }

    public async Task<IReadOnlyList<GameNotesGame>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return Array.Empty<GameNotesGame>();

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
                int schemaVersion = ReadSchemaVersion(document.RootElement);
                if (schemaVersion != CurrentSchemaVersion)
                    throw new UnsupportedGameNotesSchemaException(schemaVersion);

                stream.Position = 0;
                var file = await JsonSerializer.DeserializeAsync<GamesFile>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                if (file?.Games is null) throw new JsonException("The games collection is missing.");

                return Array.AsReadOnly(file.Games.Select(ToModel).ToArray());
            }
            catch (UnsupportedGameNotesSchemaException)
            {
                throw;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
            {
                string recoveryPath = Quarantine();
                _log?.LogWarning(exception, "Invalid game notes JSON was moved to {RecoveryPath}.", recoveryPath);
                return Array.Empty<GameNotesGame>();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(IReadOnlyList<GameNotesGame> games, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(games);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            var file = new GamesFile
            {
                SchemaVersion = CurrentSchemaVersion,
                Games = games.Select(ToDto).ToList(),
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
                    await JsonSerializer.SerializeAsync(stream, file, JsonOptions, cancellationToken)
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
        string defaultRecoveryPath = _path + ".bad";
        string recoveryPath = File.Exists(defaultRecoveryPath)
            ? _path + ".bad-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N")
            : defaultRecoveryPath;

        File.Move(_path, recoveryPath);
        return recoveryPath;
    }

    private static int ReadSchemaVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("schemaVersion", out JsonElement version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out int schemaVersion))
        {
            throw new JsonException("The game notes schema version is missing or invalid.");
        }

        return schemaVersion;
    }

    /// <summary>Rejects a record with missing fields. The whole file is then quarantined, as for any corrupt file.</summary>
    private static GameNotesGame ToModel(GameDto? game)
    {
        if (game is null
            || string.IsNullOrEmpty(game.Key)
            || game.ProcessName is null
            || game.DisplayName is null
            || game.Notes is null)
        {
            throw new JsonException("A game record is missing required fields.");
        }

        var notes = new List<GameNote>(game.Notes.Count);
        foreach (NoteDto? note in game.Notes)
        {
            if (note is null || note.Text is null) throw new JsonException("A game note is missing its text.");
            notes.Add(new GameNote(note.Id, note.Text, note.CreatedAt, note.IsPinned, note.IsCompleted));
        }

        return new GameNotesGame(game.Key, game.ProcessName, game.DisplayName, game.ExePath, game.UpdatedAt,
            GameNotesBook.OrderNotes(notes));
    }

    private static GameDto ToDto(GameNotesGame game) => new()
    {
        Key = game.Key,
        ProcessName = game.ProcessName,
        DisplayName = game.DisplayName,
        ExePath = game.ExePath,
        UpdatedAt = game.UpdatedAt,
        Notes = game.Notes.Select(note => new NoteDto
        {
            Id = note.Id,
            Text = note.Text,
            CreatedAt = note.CreatedAt,
            IsPinned = note.IsPinned,
            IsCompleted = note.IsCompleted,
        }).ToList(),
    };

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

    private sealed class GamesFile
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public List<GameDto>? Games { get; set; } = [];
    }

    private sealed class GameDto
    {
        public string? Key { get; set; }
        public string? ProcessName { get; set; }
        public string? DisplayName { get; set; }
        public string? ExePath { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public List<NoteDto>? Notes { get; set; } = [];
    }

    private sealed class NoteDto
    {
        public Guid Id { get; set; }
        public string? Text { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public bool IsPinned { get; set; }
        public bool IsCompleted { get; set; }
    }
}

public sealed class UnsupportedGameNotesSchemaException : Exception
{
    public UnsupportedGameNotesSchemaException(int schemaVersion)
        : base($"Game notes schema version {schemaVersion} is not supported.")
    {
        SchemaVersion = schemaVersion;
    }

    public int SchemaVersion { get; }
}
