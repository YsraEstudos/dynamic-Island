using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Island.App.Animations;
using Island.App.ViewModels;
using Island.Core.Notes;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;

namespace Island.App.Shell;

/// <summary>
/// The full notes app. It opens as if the widget had grown into it (see <see cref="WindowMorph"/>) and closes by
/// shrinking back into the widget.
/// </summary>
public partial class QuickNotesWindow : Window
{
    private const double ShadowMargin = 16;
    private static readonly Color WidgetColor = Color.FromRgb(0x16, 0x16, 0x16);
    private static readonly Color CardColor = Color.FromRgb(0x10, 0x13, 0x18);

    private readonly QuickNotesViewModel _viewModel;
    private readonly Func<bool> _reduceAnimations;
    private readonly WindowMorph _morph;
    private Func<Rect?>? _closeSource;
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
        _morph = new WindowMorph(Card, Surface, Shadow, WidgetColor, CardColor);
    }

    public bool HasSaveError => _viewModel.SaveState == QuickNotesSaveState.Error;

    public void FocusTitle()
    {
        Activate();
        UpdateLayout();
        TitleBox.Focus();
        Keyboard.Focus(TitleBox);
        TitleBox.CaretIndex = TitleBox.Text.Length;
    }

    /// <summary>Shows the window growing out of <paramref name="source"/>; closing shrinks into <paramref name="closeTo"/> (default: the same place).</summary>
    public void ShowOrActivate(Func<Rect?>? source, Func<Rect?>? closeTo = null)
    {
        if (IsVisible)
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Focus();
            return;
        }

        _closeSource = closeTo ?? source;
        Rect? from = source?.Invoke();

        Rect area = WindowPlacement.WorkArea;
        double width = Math.Min(Width, area.Width);
        double height = Math.Min(Height, area.Height);
        Width = width;
        Height = height;
        Left = area.Left + (area.Width - width) / 2;
        Top = area.Top + (area.Height - height) / 2;

        Show();
        var card = new Rect(Left + ShadowMargin, Top + ShadowMargin, width - ShadowMargin * 2, height - ShadowMargin * 2);
        _morph.Open(card, from, _reduceAnimations());
        Activate();
    }

    public async Task ShowNoteAsync(Guid id, Func<Rect?>? source)
    {
        ShowOrActivate(source);
        await _viewModel.OpenNoteAsync(id);
    }

    /// <summary>Opens on a blank draft. <paramref name="draftText"/> (from the capture window) is dropped into the body.</summary>
    public async Task ShowNewNoteAsync(Func<Rect?>? source, string? draftText = null, Func<Rect?>? closeTo = null)
    {
        ShowOrActivate(source, closeTo);
        await BeginNewNoteAndFocusAsync();

        if (string.IsNullOrWhiteSpace(draftText) || _viewModel.SaveState == QuickNotesSaveState.Error) return;

        _viewModel.ContentDraft = draftText;
        UpdateLayout();
        ContentBox.Focus();
        Keyboard.Focus(ContentBox);
        ContentBox.CaretIndex = ContentBox.Text.Length;
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

    public void SetHotkeyConflict(bool conflict) =>
        HotkeyConflictBanner.Visibility = conflict ? Visibility.Visible : Visibility.Collapsed;

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    private async void OnBeginNewNoteClick(object sender, RoutedEventArgs e) =>
        await BeginNewNoteAndFocusAsync();

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed) return;

        try { DragMove(); }
        catch (InvalidOperationException) { /* the button was released before the drag began */ }
    }

    private async void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            e.Handled = true;
            await BeginNewNoteAndFocusAsync();
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            e.Handled = true;
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Escape)
        {
            e.Handled = true;
            if (SearchBox.IsKeyboardFocused && SearchBox.Text.Length > 0) SearchBox.Clear();
            else Close();
        }
    }

    private async void OnNotesListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || _viewModel.IsTrashView) return;

        e.Handled = true;
        await _viewModel.TrashSelectedCommand.ExecuteAsync(null);
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
            // A failed save keeps the editor on the previous note: keep the list highlight honest too.
            if (_viewModel.SelectedNoteId != note.Id) NotesList.SelectedValue = _viewModel.SelectedNoteId;
            await AnimateEditorAsync(entering: true);
        }
        finally
        {
            _isAnimatingSelection = false;
        }
    }

    private void OnTitleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;

        e.Handled = true;
        ContentBox.Focus();
        Keyboard.Focus(ContentBox);
    }

    private void OnAddChecklistItemClick(object sender, RoutedEventArgs e) => AddChecklistItemAndFocus();

    private void OnChecklistItemKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: QuickNoteChecklistDraftItem item } box) return;

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            if (!string.IsNullOrWhiteSpace(box.Text)) AddChecklistItemAndFocus();
        }
        else if (e.Key == Key.Back && box.Text.Length == 0)
        {
            e.Handled = true;
            int index = _viewModel.ChecklistDraft.IndexOf(item);
            _viewModel.RemoveChecklistItemCommand.Execute(item);
            FocusChecklistItem(Math.Min(index, _viewModel.ChecklistDraft.Count) - 1);
        }
    }

    private void AddChecklistItemAndFocus()
    {
        _viewModel.AddChecklistItemCommand.Execute(null);
        FocusChecklistItem(_viewModel.ChecklistDraft.Count - 1);
    }

    private void FocusChecklistItem(int index)
    {
        if (index < 0 || index >= _viewModel.ChecklistDraft.Count) return;

        ChecklistItems.UpdateLayout();
        if (ChecklistItems.ItemContainerGenerator.ContainerFromIndex(index) is not DependencyObject container) return;

        TextBox? box = FindDescendant<TextBox>(container);
        if (box is null) return;

        box.Focus();
        Keyboard.Focus(box);
        box.CaretIndex = box.Text.Length;
        box.BringIntoView();
    }

    private void OnEmptyTrashClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.TrashCount == 0) return;

        var answer = System.Windows.MessageBox.Show(this,
            $"Apagar para sempre as {_viewModel.TrashCount} notas da lixeira? Isso não pode ser desfeito.",
            "Esvaziar lixeira", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes) _viewModel.EmptyTrashCommand.Execute(null);
    }

    private void OnDeletePermanentlyClick(object sender, RoutedEventArgs e)
    {
        var answer = System.Windows.MessageBox.Show(this,
            "Apagar esta nota para sempre? Isso não pode ser desfeito.",
            "Apagar nota", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes) _viewModel.DeleteSelectedPermanentlyCommand.Execute(null);
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

            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Rect card = WindowPlacement.ScreenBounds(Card)
                ?? new Rect(Left + ShadowMargin, Top + ShadowMargin, ActualWidth - ShadowMargin * 2, ActualHeight - ShadowMargin * 2);
            await _morph.CloseAsync(card, SafeSource(), _reduceAnimations());
            _allowClose = true;
            Close();
        }
        finally
        {
            _closeAttemptInProgress = false;
        }
    }

    private Rect? SafeSource()
    {
        try { return _closeSource?.Invoke(); }
        catch (Exception) { return null; }
    }

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

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }

        return null;
    }
}
