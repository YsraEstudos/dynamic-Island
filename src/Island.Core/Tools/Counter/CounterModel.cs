namespace Island.Core.Tools.Counter;

/// <summary>
/// A manual tally counter: a value that moves by a step, an optional goal, a short label and a one-step undo for the
/// last reset. Every change raises <see cref="Changed"/> and, when a store is given, queues the new state for saving.
/// Values saturate at ±<see cref="MaxValue"/> instead of overflowing. Use it from the UI thread only.
/// </summary>
public sealed class CounterModel : IDisposable
{
    public const int MaxValue = 999_999;
    public const int MaxLabelLength = 16;

    private static readonly int[] StepValues = [1, 5, 10];
    private static readonly int[] GoalValues = [0, 10, 20, 50, 100];
    private static readonly string[] LabelValues = ["Contador", "Água", "Repetições", "Partidas", "Café", "Pausas"];

    private readonly ICounterStore? _store;
    private int _value;
    private int _step;
    private int _goal;
    private string _label;
    private int? _undoValue;

    /// <param name="state">Starting state; out-of-range fields are corrected. Null starts from <see cref="CounterState.Default"/>.</param>
    /// <param name="store">Where changes are saved. The model owns it and disposes it.</param>
    public CounterModel(CounterState? state = null, ICounterStore? store = null)
    {
        CounterState start = state ?? CounterState.Default;
        _value = Clamp(start.Value);
        _step = StepValues.Contains(start.Step) ? start.Step : StepValues[0];
        _goal = Math.Clamp(start.Goal, 0, MaxValue);
        _label = NormalizeLabel(start.Label);
        _store = store;
    }

    public int Value => _value;

    public int Step => _step;

    /// <summary>The target, or 0 when there is none.</summary>
    public int Goal => _goal;

    public string Label => _label;

    public bool GoalReached => _goal > 0 && _value >= _goal;

    /// <summary>Share of the goal reached, from 0 to 1. Zero without a goal.</summary>
    public double GoalProgress => _goal > 0 ? Math.Clamp((double)_value / _goal, 0.0, 1.0) : 0.0;

    /// <summary>True after a reset, until the value moves again.</summary>
    public bool CanUndo => _undoValue is not null;

    /// <summary>Raised after every change of state, on the thread that made it.</summary>
    public event Action? Changed;

    public void Increment() => Move(_step);

    public void Decrement() => Move(-_step);

    /// <summary>Sets the value to zero and remembers the old one for <see cref="Undo"/>. Nothing happens at zero.</summary>
    public void Reset()
    {
        if (_value == 0) return;

        _undoValue = _value;
        _value = 0;
        Publish();
    }

    /// <summary>Brings back the value from before the last reset, if no count has happened since.</summary>
    public void Undo()
    {
        if (_undoValue is not int previous) return;

        _undoValue = null;
        _value = previous;
        Publish();
    }

    public void CycleStep()
    {
        int next = StepValues.FirstOrDefault(step => step > _step);
        _step = next == 0 ? StepValues[0] : next;
        Publish();
    }

    /// <summary>Moves to the next goal preset. After the last one it returns to no goal.</summary>
    public void CycleGoal()
    {
        _goal = GoalValues.FirstOrDefault(goal => goal > _goal);
        Publish();
    }

    public void CycleLabel()
    {
        int index = Array.IndexOf(LabelValues, _label);
        _label = LabelValues[(index + 1) % LabelValues.Length];
        Publish();
    }

    /// <summary>Writes a change that is still waiting for the save debounce.</summary>
    public void Flush() => _store?.Flush();

    public void Dispose() => _store?.Dispose();

    private void Move(int delta)
    {
        int next = Clamp(_value + delta);
        if (next == _value) return;

        // Counting again makes the reset permanent: undoing it would throw away these counts.
        _undoValue = null;
        _value = next;
        Publish();
    }

    private void Publish()
    {
        Changed?.Invoke();
        _store?.Save(new CounterState(_value, _step, _goal, _label));
    }

    private static int Clamp(int value) => Math.Clamp(value, -MaxValue, MaxValue);

    private static string NormalizeLabel(string? label)
    {
        string trimmed = label?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return CounterState.DefaultLabel;

        return trimmed.Length <= MaxLabelLength ? trimmed : trimmed[..MaxLabelLength];
    }
}
