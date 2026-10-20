using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Island.App.ViewModels;
using Island.Core.Pomodoro;
using Island.App.Widgets;
using TextBox = System.Windows.Controls.TextBox;

namespace Island.App.Shell;

public partial class PomodoroSessionWindow : Window
{
    private readonly PomodoroSessionViewModel _model;
    private readonly bool _reduce;

    public PomodoroSessionWindow(PomodoroSessionViewModel model, bool reduceAnimations)
    {
        InitializeComponent();
        _model = model; _reduce = reduceAnimations || !SystemParameters.ClientAreaAnimation;
        DataContext = model;
        Height = Math.Min(Height, Math.Max(420, SystemParameters.WorkArea.Height - 40));
        Loaded += (_, _) =>
        {
            if (_reduce) return;
            Surface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)));
            Entrance.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(340)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        };
        model.PropertyChanged += OnModelChanged;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PomodoroSessionViewModel.SelectedProfile) || _reduce) return;
        ProfilePicker.BeginAnimation(OpacityProperty, new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(260)));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Island.Windows.Display.WindowAppearance.EnableDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        GoalBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _model.PropertyChanged -= OnModelChanged;
        base.OnClosed(e);
    }

    private void OnDone(object sender, RoutedEventArgs e) => Close();

    public static void ShowFor(ShelfContext context)
    {
        var model = new PomodoroSessionViewModel(context.Pomodoro, context.Angry, context.Settings, context.ApplySettings);
        var window = new PomodoroSessionWindow(model, context.Settings().ReduceAnimations);
        using var signal = new UiSignal(window.Dispatcher, model.Refresh);
        void Changed() => signal.Signal();
        context.Pomodoro.Changed += Changed;
        context.Angry.LockChanged += Changed;
        try { window.ShowDialog(); }
        finally
        {
            context.Pomodoro.Changed -= Changed;
            context.Angry.LockChanged -= Changed;
        }
    }
}
