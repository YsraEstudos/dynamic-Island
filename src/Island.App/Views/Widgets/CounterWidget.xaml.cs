using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Island.App.Widgets;
using Island.Core.Tools.Counter;
using Island.Windows.Tools.Counter;
using UserControl = System.Windows.Controls.UserControl;

namespace Island.App.Views.Widgets;

/// <summary>
/// Manual tally counter (240 x 152). The count lives in a <see cref="CounterModel"/> kept in the shelf's tool registry,
/// so it survives the shelf being rebuilt. The label and the step cycle on click, a right click on the number cycles
/// the goal, and the wheel over the number steps it. After a reset, "Desfazer" takes the reset button's place for a
/// few seconds.
/// </summary>
public partial class CounterWidget : UserControl
{
    /// <summary>Long enough to catch a mistaken reset, short enough to stay out of the way.</summary>
    private static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(4);
    private static readonly Geometry MinusGeometry = Frozen(Geometry.Parse("M5,12 H19"));

    private readonly ShelfContext _context;
    private readonly CounterModel _model;
    private readonly DispatcherTimer _undoTimer = new() { Interval = UndoWindow };
    private bool _undoShown;

    public CounterWidget(ShelfContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        InitializeComponent();
        _context = context;
        _model = context.Tools.GetOrCreate(() => CreateModel());
        _undoTimer.Tick += (_, _) => HideUndo();

        LabelButton.Click += () => _model.CycleLabel();
        StepButton.Click += () => _model.CycleStep();
        MinusButton.Icon = MinusGeometry;
        MinusButton.Click += () => Adjust(-1);
        PlusButton.Click += () => Adjust(1);
        ResetButton.Click += () =>
        {
            _model.Reset();
            if (_model.CanUndo) ShowUndo();
        };
        UndoButton.Click += () =>
        {
            _model.Undo();
            HideUndo();
        };
        NumberDisplay.MouseWheel += (_, e) =>
        {
            e.Handled = true;
            Adjust(e.Delta > 0 ? 1 : -1);
        };
        NumberDisplay.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            _model.CycleGoal();
        };
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }

    private static CounterModel CreateModel()
    {
        var store = new CounterStore();
        return new CounterModel(store.Load(), store);
    }

    private void Subscribe()
    {
        _model.Changed += OnModelChanged;
        Render(animate: false);
    }

    private void Unsubscribe()
    {
        _model.Changed -= OnModelChanged;
        _model.Flush();
        HideUndo();
    }

    private void OnModelChanged() => Render(animate: true);

    private void Adjust(int direction)
    {
        int before = _model.Value;
        if (direction > 0) _model.Increment();
        else _model.Decrement();

        // Pulse only when the count moved; at the limit a tap has nothing to show.
        if (_model.Value != before) NumberDisplay.Pulse();
    }

    private void Render(bool animate)
    {
        LabelButton.LabelText = _model.Label;
        StepButton.LabelText = CounterFormat.StepText(_model.Step);

        bool reduceMotion = _context.Settings().ReduceAnimations;
        if (animate) NumberDisplay.Show(_model, reduceMotion);
        else NumberDisplay.Restore(_model, reduceMotion);

        UpdateUndoButtons();
    }

    private void ShowUndo()
    {
        _undoShown = true;
        _undoTimer.Stop();
        _undoTimer.Start();
        UpdateUndoButtons();
    }

    private void HideUndo()
    {
        _undoShown = false;
        _undoTimer.Stop();
        UpdateUndoButtons();
    }

    private void UpdateUndoButtons()
    {
        bool undo = _undoShown && _model.CanUndo;
        ResetButton.Visibility = undo ? Visibility.Collapsed : Visibility.Visible;
        UndoButton.Visibility = undo ? Visibility.Visible : Visibility.Collapsed;
    }

    private static T Frozen<T>(T value) where T : Freezable
    {
        value.Freeze();
        return value;
    }
}
