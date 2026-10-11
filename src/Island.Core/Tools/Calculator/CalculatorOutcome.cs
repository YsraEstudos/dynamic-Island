namespace Island.Core.Tools.Calculator;

/// <summary>What a key press changed, so the widget knows which motion to play.</summary>
public enum CalculatorOutcome
{
    /// <summary>Nothing changed: a blocked key, or a key with no effect in this state.</summary>
    None,

    /// <summary>The number being typed changed. Shown at once, without motion.</summary>
    Entry,

    /// <summary>= or an operator computed a new value. The widget slides it in.</summary>
    Result,

    /// <summary>The calculation failed (division by zero or a number out of range). The display reads "Erro".</summary>
    Error,

    /// <summary>C cleared everything.</summary>
    Cleared,
}
