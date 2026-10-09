using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;
using Island.App.ViewModels;
using Island.Core.Notes;

namespace Island.App.Shell;

public partial class QuickNotesWindow : Window
{
    private readonly QuickNotesViewModel _viewModel;
    private readonly Func<bool> _reduceAnimations;
    private bool _allowClose;
    private bool _closeAttemptInProgress;
    private bool _isAnimatingSelection;
    private bool _beginNewInProgress;

    public QuickNotesWindow(QuickNotesViewModel viewModel, Func<bool> reduceAnimations)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _reduceAnimations = reduceAnimations ?? throw new ArgumentNullException(nameof(reduceAnimations));
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    public void FocusTitle()
    {
        Activate();
        TitleBox.Focus();
        TitleBox.CaretIndex = TitleBox.Text.Length;
    }

    public bool HasSaveError => _viewModel.SaveState == QuickNotesSaveState.Error;

    public void ShowOrActivate()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    public Task FlushPendingSaveAsync() => _viewModel.FlushPendingSaveAsync();

    public async Task BeginNewNoteAndFocusAsync()
    {
        if (_beginNewInProgress) return;

        _beginNewInProgress = true;
        Surface.IsEnabled = false;
        bool focusNewDraft = false;
        try
        {
            await _viewModel.BeginNewCommand.ExecuteAsync(null);
            focusNewDraft = _viewModel.SaveState != QuickNotesSaveState.Error;
        }
        finally
        {
            Surface.IsEnabled = true;
            _beginNewInProgress = false;
        }

        if (focusNewDraft) FocusTitle();
    }

    private async void OnBeginNewNoteClick(object sender, RoutedEventArgs e) =>
        await BeginNewNoteAndFocusAsync();

    public void SetHotkeyConflict(bool conflict) =>
        HotkeyConflictBanner.Visibility = conflict ? Visibility.Visible : Visibility.Collapsed;

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_reduceAnimations())
        {
            Surface.Opacity = 1;
            SurfaceTranslation.Y = 0;
        }
        else
        {
            Surface.Opacity = 0;
            SurfaceTranslation.Y = 12;
            AnimateSurface(entering: true);
        }
    }

    private async void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            e.Handled = true;
            await BeginNewNoteAndFocusAsync();
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private async void OnNotesSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isAnimatingSelection || NotesList.SelectedItem is not QuickNote note || _viewModel.SelectedNoteId == note.Id)
            return;

        _isAnimatingSelection = true;
        try
        {
            await AnimateEditorAsync(entering: false);
            await _viewModel.SelectNoteCommand.ExecuteAsync(note.Id);
            await AnimateEditorAsync(entering: true);
        }
        finally
        {
            _isAnimatingSelection = false;
        }
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;

        e.Cancel = true;
        if (_closeAttemptInProgress) return;

        _closeAttemptInProgress = true;
        Surface.IsEnabled = false;
        try
        {
            await _viewModel.FlushPendingSaveAsync();
            if (_viewModel.SaveState == QuickNotesSaveState.Error)
            {
                Surface.IsEnabled = true;
                return;
            }

            if (!_reduceAnimations()) await AnimateSurfaceAsync(entering: false);
            _allowClose = true;
            Close();
        }
        finally
        {
            _closeAttemptInProgress = false;
        }
    }

    private void AnimateSurface(bool entering)
    {
        var duration = TimeSpan.FromMilliseconds(190);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        Surface.BeginAnimation(OpacityProperty, new DoubleAnimation(entering ? 1 : 0, duration) { EasingFunction = easing });
        SurfaceTranslation.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(entering ? 0 : 10, duration) { EasingFunction = easing });
    }

    private Task AnimateSurfaceAsync(bool entering) =>
        AnimateElementAsync(Surface, SurfaceTranslation, entering ? 1 : 0, entering ? 0 : 10, 180);

    private Task AnimateEditorAsync(bool entering)
    {
        if (_reduceAnimations())
        {
            EditorPanel.Opacity = 1;
            EditorTranslation.Y = 0;
            return Task.CompletedTask;
        }

        return AnimateElementAsync(EditorPanel, EditorTranslation, entering ? 1 : 0, entering ? 0 : 6, 110);
    }

    private static Task AnimateElementAsync(FrameworkElement element, TranslateTransform translation, double opacity, double y, int durationMilliseconds)
    {
        if (element.RenderTransform is not TranslateTransform)
            element.RenderTransform = translation;

        var duration = TimeSpan.FromMilliseconds(durationMilliseconds);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var opacityAnimation = new DoubleAnimation(opacity, duration) { EasingFunction = easing };
        var moveAnimation = new DoubleAnimation(y, duration) { EasingFunction = easing };
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remaining = 2;
        EventHandler complete = (_, _) =>
        {
            if (Interlocked.Decrement(ref remaining) == 0) completion.TrySetResult();
        };
        opacityAnimation.Completed += complete;
        moveAnimation.Completed += complete;
        element.BeginAnimation(OpacityProperty, opacityAnimation);
        translation.BeginAnimation(TranslateTransform.YProperty, moveAnimation);
        return completion.Task;
    }
}
