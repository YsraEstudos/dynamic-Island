using System.Globalization;

namespace Island.Core.Tools.Calculator;

/// <summary>
/// State machine of a pocket calculator. Operations run as they are entered, left to right (2 + 3 × 4 gives 20),
/// not with operator precedence. Values are decimal, so 0,1 + 0,2 is exactly 0,3.
/// </summary>
public sealed class CalculatorEngine
{
    /// <summary>Digits a typed number may have. Results are rounded to the same count of significant digits.</summary>
    public const int MaxDigits = 12;

    // Magnitudes at or above this do not fit the display; they end in the error state instead.
    private static readonly decimal Limit = 1_000_000_000_000m;
    private const int MaxDecimalPlaces = 28;

    private decimal _accumulator;          // the value shown when nothing is being typed
    private CalculatorKey? _pending;       // operator waiting for its second operand
    private string? _entry;                // number being typed, invariant format: "-12.50", "0."
    private CalculatorKey? _lastOperator;  // repeated by = when no operator is pending
    private decimal _lastOperand;
    private bool _error;
    private bool _entryIsResult;

    public bool IsError => _error;

    /// <summary>The operator just pressed. Highlighted until the next number starts.</summary>
    public CalculatorKey? ActiveOperator { get; private set; }

    /// <summary>The number on the display, formatted for pt-BR.</summary>
    public string DisplayText => _error
        ? CalculatorDisplayFormatter.ErrorText
        : _entry is null
            ? CalculatorDisplayFormatter.FormatValue(_accumulator)
            : EntryDisplay;

    /// <summary>The calculation in progress, such as "12 × 3". Empty when no operator is pending.</summary>
    public string Expression
    {
        get
        {
            if (_error || _pending is not { } op) return string.Empty;

            string left = CalculatorDisplayFormatter.FormatValue(_accumulator);
            string symbol = CalculatorDisplayFormatter.OperatorSymbol(op);
            return _entry is null
                ? $"{left} {symbol}"
                : $"{left} {symbol} {EntryDisplay}";
        }
    }

    public CalculatorOutcome Press(CalculatorKey key)
    {
        if (_error)
        {
            // Only C or a digit leaves the error, so a stray operator cannot continue a broken sum.
            if (key == CalculatorKey.Clear) return Reset();
            if (!IsDigit(key)) return CalculatorOutcome.None;
            Reset();
        }

        return key switch
        {
            CalculatorKey.Clear => Reset(),
            CalculatorKey.Backspace => Backspace(),
            CalculatorKey.ToggleSign => ToggleSign(),
            CalculatorKey.Percent => Percent(),
            CalculatorKey.DecimalPoint => AppendDecimalPoint(),
            CalculatorKey.Equals => Evaluate(),
            CalculatorKey.Add or CalculatorKey.Subtract or CalculatorKey.Multiply or CalculatorKey.Divide => Operate(key),
            _ => AppendDigit((int)key - (int)CalculatorKey.D0),
        };
    }

    private string EntryDisplay => _entryIsResult
        ? CalculatorDisplayFormatter.FormatValue(ParseEntry(_entry!))
        : CalculatorDisplayFormatter.FormatEntry(_entry!);

    private CalculatorOutcome Reset()
    {
        _accumulator = 0m;
        _pending = null;
        _entry = null;
        _lastOperator = null;
        _lastOperand = 0m;
        _error = false;
        ActiveOperator = null;
        return CalculatorOutcome.Cleared;
    }

    private CalculatorOutcome AppendDigit(int digit)
    {
        string digitText = digit.ToString(CultureInfo.InvariantCulture);
        if (_entry is null || _entryIsResult)
        {
            _entry = digitText;
        }
        else if (_entry.TrimStart('-') == "0")
        {
            // A typed 0 is replaced, so "0" then "5" reads 5 and not 05.
            _entry = (_entry.StartsWith('-') ? "-" : string.Empty) + digitText;
        }
        else if (_entry.Count(char.IsAsciiDigit) < MaxDigits)
        {
            _entry += digitText;
        }
        else
        {
            return CalculatorOutcome.None;
        }

        ActiveOperator = null;
        _entryIsResult = false;
        return CalculatorOutcome.Entry;
    }

    private CalculatorOutcome AppendDecimalPoint()
    {
        _entryIsResult = false;
        if (_entry is null)
        {
            _entry = "0.";
        }
        else if (_entry.Contains('.'))
        {
            return CalculatorOutcome.None;
        }
        else
        {
            _entry += ".";
        }

        ActiveOperator = null;
        return CalculatorOutcome.Entry;
    }

    private CalculatorOutcome ToggleSign()
    {
        // Keep the operand editable, but format a generated result as a result.
        _entryIsResult = _entry is null || _entryIsResult;
        _entry = _entry is null
            ? ToEntry(-_accumulator)
            : _entry.StartsWith('-') ? _entry[1..] : "-" + _entry;

        ActiveOperator = null;
        return CalculatorOutcome.Entry;
    }

    private CalculatorOutcome Backspace()
    {
        _entryIsResult = false;
        if (_entry is null || _entry == "0") return CalculatorOutcome.None;

        string rest = _entry[..^1];
        _entry = rest is "" or "-" ? "0" : rest;
        return CalculatorOutcome.Entry;
    }

    private CalculatorOutcome Percent()
    {
        // "200 + 10 %" takes 10 % of 200, as a pocket calculator does. Other cases are simply value / 100.
        decimal? percent = _pending is CalculatorKey.Add or CalculatorKey.Subtract && _entry is not null
            ? _accumulator * ParseEntry(_entry) / 100m
            : CurrentValue() / 100m;

        decimal? normalized = Normalize(percent);
        if (normalized is null) return Fail();

        _entry = ToEntry(normalized.Value);
        _entryIsResult = true;
        ActiveOperator = null;
        return CalculatorOutcome.Entry;
    }

    private CalculatorOutcome Operate(CalculatorKey op)
    {
        if (_pending is null)
        {
            _accumulator = CurrentValue();
            _entry = null;
            _pending = op;
            ActiveOperator = op;
            return CalculatorOutcome.Entry;
        }

        if (_entry is null)
        {
            // "2 + ×" only changes the operator.
            _pending = op;
            ActiveOperator = op;
            return CalculatorOutcome.None;
        }

        // Left to right: the pending operation completes before the new operator takes over.
        return Commit(Apply(_accumulator, _pending.Value, ParseEntry(_entry)), nextOperator: op);
    }

    private CalculatorOutcome Evaluate()
    {
        if (_pending is { } op)
        {
            // "2 + =" uses the shown value as the second operand, and = again repeats the operation.
            _lastOperator = op;
            _lastOperand = CurrentValue();
            return Commit(Apply(_accumulator, op, _lastOperand), nextOperator: null);
        }

        if (_lastOperator is { } repeated)
        {
            return Commit(Apply(CurrentValue(), repeated, _lastOperand), nextOperator: null);
        }

        return CalculatorOutcome.None;
    }

    private CalculatorOutcome Commit(decimal? raw, CalculatorKey? nextOperator)
    {
        decimal? result = Normalize(raw);
        if (result is null) return Fail();

        _accumulator = result.Value;
        _entry = null;
        _pending = nextOperator;
        ActiveOperator = nextOperator;
        return CalculatorOutcome.Result;
    }

    private CalculatorOutcome Fail()
    {
        _error = true;
        _accumulator = 0m;
        _entry = null;
        _pending = null;
        _lastOperator = null;
        _lastOperand = 0m;
        ActiveOperator = null;
        return CalculatorOutcome.Error;
    }

    private decimal CurrentValue() => _entry is null ? _accumulator : ParseEntry(_entry);

    /// <summary>Null when the result does not fit: division by zero, or a magnitude the display cannot show.</summary>
    private static decimal? Apply(decimal left, CalculatorKey op, decimal right)
    {
        try
        {
            return op switch
            {
                CalculatorKey.Add => left + right,
                CalculatorKey.Subtract => left - right,
                CalculatorKey.Multiply => left * right,
                _ => right == 0m ? null : left / right,
            };
        }
        catch (OverflowException)
        {
            // decimal overflows only on a huge quotient, such as a number divided by a tiny one.
            return null;
        }
    }

    /// <summary>Rounds to <see cref="MaxDigits"/> significant digits. Null when the magnitude is out of range.</summary>
    private static decimal? Normalize(decimal? value)
    {
        if (value is not { } v || Math.Abs(v) >= Limit) return null;
        if (v == 0m) return 0m;

        int magnitude = (int)Math.Floor(Math.Log10((double)Math.Abs(v)));
        int places = Math.Min(MaxDigits - 1 - magnitude, MaxDecimalPlaces);
        decimal rounded = Math.Round(v, places, MidpointRounding.AwayFromZero);
        return Math.Abs(rounded) >= Limit ? null : rounded;
    }

    private static decimal ParseEntry(string entry) =>
        decimal.Parse(entry.TrimEnd('.'), NumberStyles.Number, CultureInfo.InvariantCulture);

    private static string ToEntry(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static bool IsDigit(CalculatorKey key) => key is >= CalculatorKey.D0 and <= CalculatorKey.D9;
}
