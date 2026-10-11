namespace Island.Core.Tools.Counter;

/// <summary>What the counter keeps between runs. <see cref="CounterModel"/> corrects out-of-range fields on load.</summary>
public sealed record CounterState(int Value, int Step, int Goal, string Label)
{
    public const string DefaultLabel = "Contador";

    public static CounterState Default { get; } = new(0, 1, 0, DefaultLabel);
}
