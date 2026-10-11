using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Island.App.Widgets;
using Island.Core.Tools.Calculator;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace Island.App.Views.Widgets;

/// <summary>
/// The calculator's 4 x 5 keypad, built in code: one <see cref="ShelfButton"/> per key, so each key springs when
/// pressed. The operator that produced the current number is inverted until the next number starts.
/// </summary>
public sealed class CalculatorKeypad : UniformGrid
{
    private const double KeyRadius = 9;

    private static readonly (string Label, CalculatorKey Key)[] Layout =
    {
        ("C", CalculatorKey.Clear), ("⌫", CalculatorKey.Backspace), ("%", CalculatorKey.Percent), ("÷", CalculatorKey.Divide),
        ("7", CalculatorKey.D7), ("8", CalculatorKey.D8), ("9", CalculatorKey.D9), ("×", CalculatorKey.Multiply),
        ("4", CalculatorKey.D4), ("5", CalculatorKey.D5), ("6", CalculatorKey.D6), ("−", CalculatorKey.Subtract),
        ("1", CalculatorKey.D1), ("2", CalculatorKey.D2), ("3", CalculatorKey.D3), ("+", CalculatorKey.Add),
        ("±", CalculatorKey.ToggleSign), ("0", CalculatorKey.D0), (",", CalculatorKey.DecimalPoint), ("=", CalculatorKey.Equals),
    };

    private static readonly Brush DigitBackground = Frozen(0x2C, 0x2C, 0x2E);
    private static readonly Brush FunctionBackground = Frozen(0x48, 0x48, 0x4A);

    private readonly Dictionary<CalculatorKey, ShelfButton> _operators = new();
    private readonly Brush _orange;

    public CalculatorKeypad()
    {
        Columns = 4;
        Rows = 5;
        _orange = (Brush)FindResource("AccentOrangeBrush");

        foreach ((string label, CalculatorKey key) in Layout)
        {
            bool isOperator = IsOperator(key);
            var button = new ShelfButton
            {
                LabelText = label,
                LabelBrush = isOperator ? _orange : Brushes.White,
                Background = BackgroundFor(key),
                CornerRadius = new CornerRadius(KeyRadius),
                Margin = new Thickness(1),
            };
            button.Click += () => KeyPressed?.Invoke(key);
            Children.Add(button);

            if (isOperator) _operators[key] = button;
        }
    }

    /// <summary>Raised with the key that was clicked.</summary>
    public event Action<CalculatorKey>? KeyPressed;

    /// <summary>Inverts the operator button that produced the current calculation. Pass null to show none.</summary>
    public void HighlightOperator(CalculatorKey? active)
    {
        foreach ((CalculatorKey key, ShelfButton button) in _operators)
        {
            button.Background = key == active ? Brushes.White : DigitBackground;
        }
    }

    private Brush BackgroundFor(CalculatorKey key)
    {
        if (key == CalculatorKey.Equals) return _orange;
        if (IsOperator(key) || IsDigit(key) || key == CalculatorKey.DecimalPoint) return DigitBackground;
        return FunctionBackground;
    }

    private static bool IsOperator(CalculatorKey key) =>
        key is CalculatorKey.Add or CalculatorKey.Subtract or CalculatorKey.Multiply or CalculatorKey.Divide;

    private static bool IsDigit(CalculatorKey key) => key is >= CalculatorKey.D0 and <= CalculatorKey.D9;

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
