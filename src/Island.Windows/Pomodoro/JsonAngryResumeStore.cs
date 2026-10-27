using System.Text.Json;
using System.Text.Json.Serialization;
using Island.Core.Pomodoro;
using Island.Windows.Storage;

namespace Island.Windows.Pomodoro;

/// <summary>
/// Carries an Angry session across an app restart, such as an update. The old process saves it just before the new
/// one launches, and the new process takes it once at startup. The file is removed on every take, so it is used once.
/// </summary>
public sealed class JsonAngryResumeStore(string path)
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Writes the session. Returns false when the file could not be written.</summary>
    public bool TrySave(AngryResumeState state, DateTimeOffset savedAt)
    {
        try
        {
            AtomicJsonFile.Write(path, new StoreFile { SavedAt = savedAt, State = state }, Options);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceError("Could not save the Angry session: {0}", ex);
            return false;
        }
    }

    /// <summary>
    /// Reads the saved session and removes the file. Returns null when there is none, when it was saved outside
    /// <paramref name="maxAge"/> before <paramref name="now"/>, or when the file is unreadable.
    /// </summary>
    public AngryResumeState? Take(DateTimeOffset now, TimeSpan maxAge)
    {
        try
        {
            if (!File.Exists(path)) return null;
            StoreFile? file = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(path), Options);
            if (file?.State is null) return null;

            TimeSpan age = now - file.SavedAt;
            return age >= TimeSpan.Zero && age <= maxAge ? file.State : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Trace.TraceError("Could not read the Angry session: {0}", ex);
            return null;
        }
        finally
        {
            Discard();
        }
    }

    private void Discard()
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class StoreFile
    {
        public DateTimeOffset SavedAt { get; set; }
        public AngryResumeState? State { get; set; }
    }
}
