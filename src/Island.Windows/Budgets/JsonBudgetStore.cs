using System.Text.Json;
using System.Text.Json.Serialization;
using Island.Core.Budgets;

namespace Island.Windows.Budgets;

/// <summary>Budgets in %LocalAppData%\DynamicIsland\budgets.json, written atomically (.tmp + move).</summary>
public sealed class JsonBudgetStore : IBudgetStore
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private readonly string _path;

    public JsonBudgetStore(string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland");
        _path = Path.Combine(directory, "budgets.json");
    }

    public BudgetsData Load()
    {
        try
        {
            if (!File.Exists(_path)) return Empty();
            var data = JsonSerializer.Deserialize<BudgetsData>(File.ReadAllText(_path), Options);
            return data is null ? Empty() : Sanitize(data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or JsonException or NotSupportedException)
        {
            return Empty();
        }
    }

    public void Save(BudgetsData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));
        File.Move(tmp, _path, overwrite: true);
    }

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
