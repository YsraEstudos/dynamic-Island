using System.Text.Json;
using Island.Windows.Storage;
using System.Text.Json.Serialization;
using Island.Core.Budgets;

namespace Island.Windows.Budgets;

/// <summary>Budgets in %LocalAppData%\DynamicIsland\budgets.json, written atomically (.tmp + move).</summary>
public sealed class JsonBudgetStore : IBudgetStore
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private readonly string _path;
    private readonly object _gate = new();
    private Exception? _loadError;

    public JsonBudgetStore(string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland");
        _path = Path.Combine(directory, "budgets.json");
    }

    public BudgetsData Load()
    {
        lock (_gate)
        {
            _loadError = null;
            try
            {
                string json = File.ReadAllText(_path);
                var data = JsonSerializer.Deserialize<BudgetsData>(json, Options)
                    ?? throw new JsonException("Missing budget data.");
                if (data.Budgets is null) throw new JsonException("Missing budget collection.");
                return Sanitize(data);
            }
            catch (FileNotFoundException) { return Empty(); }
            catch (DirectoryNotFoundException) { return Empty(); }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                try { AtomicJsonFile.Quarantine(_path); }
                catch (Exception recovery) when (recovery is IOException or UnauthorizedAccessException) { _loadError = recovery; }
                return Empty();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _loadError = ex;
                return Empty();
            }
        }
    }

    public void Save(BudgetsData data)
    {
        lock (_gate)
        {
            if (_loadError is not null) throw new IOException("Budgets could not be read; writes are disabled until a successful reload.", _loadError);
            AtomicJsonFile.Write(_path, data, Options);
        }
    }

    public string? LoadError => _loadError?.Message;

    private static BudgetsData Empty() => new(new List<Budget>(), null);

    private static BudgetsData Sanitize(BudgetsData data) =>
        new(data.Budgets?.Where(b => b is not null).Select(Repair).ToList() ?? new List<Budget>(), data.SelectedId);

    private static Budget Repair(Budget budget) =>
        budget with { ExcludedDays = budget.ExcludedDays ?? new List<DateOnly>() };

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
