using System.Text;
using System.Text.Json;
using Island.Windows.Storage;
using System.Globalization;
using Island.Core.Abstractions;
using Island.Core.Calendar;

namespace Island.Windows.Calendar;

/// <summary>Stores the calendar separately from application settings.</summary>
public sealed class JsonCalendarStore : ICalendarStore
{
    private const string FileName = "calendar.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _directory;
    private readonly string _path;
    private readonly string _badPath;
    private readonly object _gate = new();

    /// <param name="directory">Defaults to %LocalAppData%\DynamicIsland.</param>
    public JsonCalendarStore(string? directory = null)
    {
        _directory = directory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland");
        _path = Path.Combine(_directory, FileName);
        _badPath = _path + ".bad";
    }

    public CalendarData Load()
    {
        lock (_gate)
        {
            string json;
            try { json = File.ReadAllText(_path, Encoding.UTF8); }
            catch (FileNotFoundException) { return CalendarData.Empty; }
            catch (DirectoryNotFoundException) { return CalendarData.Empty; }
            CalendarData? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<CalendarData>(json, ReadOptions);
            }
            catch (JsonException)
            {
                // Preserve malformed user data before exposing an empty agenda.
                File.Move(_path, GetRecoveryPath());
                return CalendarData.Empty;
            }

            return parsed ?? CalendarData.Empty;
        }
    }

    public void Save(CalendarData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (_gate) AtomicJsonFile.Write(_path, data, WriteOptions);
    }



    private string GetRecoveryPath()
    {
        if (!File.Exists(_badPath)) return _badPath;

        string suffix = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N");
        return _badPath + "-" + suffix;
    }
}
