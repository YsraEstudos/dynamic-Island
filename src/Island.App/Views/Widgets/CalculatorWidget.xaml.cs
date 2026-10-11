using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Island.App.Widgets;
using Island.Core.Tools.Calculator;
using Brush = System.Windows.Media.Brush;
using UserControl = System.Windows.Controls.UserControl;

namespace Island.App.Views.Widgets;

/// <summary>
/// Pocket calculator on the shelf. The engine lives in the tool registry, so a number typed while the shelf is
/// rebuilt is still there. The island cannot take keyboard focus, so every key is a button.
/// </summary>
public partial class CalculatorWidget : UserControl
{
    private readonly ShelfContext _context;
    private readonly CalculatorEngine _engine;
    private readonly CalculatorKeypad _keypad = new();
    private readonly CalculatorDisplayMotion _motion;
    private readonly Brush _primary;
    private readonly Brush _error;

    public CalculatorWidget(ShelfContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        InitializeComponent();
        _context = context;
        _engine = context.Tools.GetOrCreate(() => new CalculatorEngine());
        _motion = new CalculatorDisplayMotion(ValueText);
        _primary = (Brush)FindResource("TextPrimaryBrush");
        _error = (Brush)FindResource("MutedRedBrush");

        KeypadHost.Children.Add(_keypad);
        _keypad.KeyPressed += OnKeyPressed;
        // The wheel over the number does nothing, so it does not scroll the shelf underneath.
        DisplayPanel.MouseWheel += (_, e) => e.Handled = true;
        DisplayPanel.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            CopyResult();
        };

        Show(CalculatorOutcome.None, string.Empty, animate: false);
    }

    private void OnKeyPressed(CalculatorKey key)
    {
        string before = _engine.DisplayText;
        CalculatorOutcome outcome = _engine.Press(key);
        Show(outcome, before, animate: !_context.Settings().ReduceAnimations);
    }

    /// <summary>
    /// Redraws the display. A new result slides in and an error shakes; typed digits appear at once.
    /// The font shrinks smoothly as the number grows, unless animations are reduced.
    /// </summary>
    private void Show(CalculatorOutcome outcome, string before, bool animate)
    {
        string text = _engine.DisplayText;
        ExpressionText.Text = _engine.Expression;
        ValueText.Text = text;
        ValueText.Foreground = _engine.IsError ? _error : _primary;
        _keypad.HighlightOperator(_engine.ActiveOperator);
        _motion.SetFontSize(CalculatorDisplayFormatter.FontSizeFor(text), animate);

        if (!animate)
        {
            _motion.Reset();
            return;
        }

        if (outcome == CalculatorOutcome.Error) _motion.Shake();
        else if (outcome == CalculatorOutcome.Result && text != before) _motion.Slide();
    }

    private void CopyResult()
    {
        if (_engine.IsError) return;

        try
        {
            System.Windows.Clipboard.SetText(_engine.DisplayText);
        }
        catch (ExternalException)
        {
            // Another program holds the clipboard; the user can click again.
        }
    }
}
