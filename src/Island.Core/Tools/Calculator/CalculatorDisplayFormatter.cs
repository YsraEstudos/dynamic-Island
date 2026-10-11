using System.Globalization;
using System.Text;

namespace Island.Core.Tools.Calculator;

/// <summary>Text of the calculator display in pt-BR. Kept apart from WPF so the wording and grouping are testable.</summary>
public static class CalculatorDisplayFormatter
{
    public const string ErrorText = "Erro";

    // Below this magnitude a value reads better as 1,5E-7 than as a row of zeros.
    private const decimal ScientificBelow = 0.000001m;
    private const double FullFontSize = 22;
    private const int FullFontLength = 9;
    private const double MinFontSize = 14;

    private static readonly NumberFormatInfo PtBr = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NegativeSign = "-",
    };

    /// <summary>A finished value: thousands with dots, no trailing zeros, scientific notation for very small values.</summary>
    public static string FormatValue(decimal value)
    {
        if (value == 0m) return "0";
        if (Math.Abs(value) < ScientificBelow) return FormatScientific(value);
        return value.ToString("#,##0.##################", PtBr);
    }

    /// <summary>
    /// The number being typed, as the user sees it while typing: "1.234,"  or  "0,50" keep what was typed after
    /// the comma, so a decimal point and its zeros are not swallowed.
    /// </summary>
    public static string FormatEntry(string entry)
    {
        bool negative = entry.StartsWith('-');
        string unsigned = negative ? entry[1..] : entry;
        int point = unsigned.IndexOf('.');

        string whole = point < 0 ? unsigned : unsigned[..point];
        string grouped = decimal.Parse(whole, NumberStyles.None, CultureInfo.InvariantCulture).ToString("#,##0", PtBr);
        string fraction = point < 0 ? string.Empty : "," + unsigned[(point + 1)..];

        return (negative ? "-" : string.Empty) + grouped + fraction;
    }

    /// <summary>Font size that keeps a number on one line. Shrinks smoothly as the text grows.</summary>
    public static double FontSizeFor(string text)
    {
        if (text.Length <= FullFontLength) return FullFontSize;
        return Math.Max(MinFontSize, FullFontSize * FullFontLength / text.Length);
    }

    public static string OperatorSymbol(CalculatorKey key) => key switch
    {
        CalculatorKey.Add => "+",
        CalculatorKey.Subtract => "−",
        CalculatorKey.Multiply => "×",
        CalculatorKey.Divide => "÷",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Not an operator key."),
    };

    private static string FormatScientific(decimal value)
    {
        decimal mantissa = Math.Abs(value);
        int exponent = 0;
        while (mantissa < 1m)
        {
            mantissa *= 10m;
            exponent--;
        }

        mantissa = Math.Round(mantissa, 6, MidpointRounding.AwayFromZero);
        if (mantissa >= 10m)
        {
            // Rounding can carry into a new digit, as in 9,9999996 becoming 10.
            mantissa /= 10m;
            exponent++;
        }

        var text = new StringBuilder();
        if (value < 0m) text.Append('-');
        text.Append(mantissa.ToString("0.######", PtBr));
        text.Append('E').Append(exponent.ToString(CultureInfo.InvariantCulture));
        return text.ToString();
    }
}
