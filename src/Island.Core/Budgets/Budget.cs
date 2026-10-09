using System.Text.Json.Serialization;

namespace Island.Core.Budgets;

public sealed record Budget
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Nova IA";
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public int ResetHour { get; init; } = 4;
    public double TotalPercent { get; init; } = 100;
    public EntryMode Mode { get; init; } = EntryMode.Consumed;
    public double EnteredValue { get; init; }
    public List<DateOnly> ExcludedDays { get; init; } = new();

    [JsonIgnore]
    public double Used => Mode == EntryMode.Consumed ? Capped : Ceiling - Capped;

    [JsonIgnore]
    public double Remaining => Ceiling - Used;

    private double Ceiling => Math.Max(TotalPercent, 0);

    private double Capped =>
        Math.Clamp(double.IsNaN(EnteredValue) ? 0 : EnteredValue, 0, Ceiling);
}
