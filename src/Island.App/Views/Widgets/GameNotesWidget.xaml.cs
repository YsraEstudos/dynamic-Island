using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Island.App.GameNotes;
using Island.App.Widgets;
using Island.Core.GameNotes;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Path = System.Windows.Shapes.Path;
using UserControl = System.Windows.Controls.UserControl;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Island.App.Views.Widgets;

/// <summary>
/// Shelf entry for the notes of the game in front. The game stays on screen after focus moves to the island (the
/// last game detected). Typing happens in the capture window (+ or Ctrl+Alt+G): the island cannot take keyboard focus.
/// </summary>
public partial class GameNotesWidget : UserControl
{
    /// <summary>Rows that fit above the footer. With more notes, the last row becomes the "+N notas" line.</summary>
    private const int MaxRows = 3;
    private const int SwitchOutMilliseconds = 110;
    private const int SwitchInMilliseconds = 170;
    private const int CheckMilliseconds = 150;

    private static readonly Color BlueColor = Color.FromRgb(0x65, 0xB6, 0xFF);
    private static readonly Brush BlueBrush = Frozen(new SolidColorBrush(BlueColor));
    private static readonly Brush YellowBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0xC4, 0x51)));
    private static readonly Brush MutedBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x68, 0x70, 0x7E)));
    private static readonly Brush InkBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x16)));
    private static readonly Brush PinnedBackground = Frozen(new SolidColorBrush(Color.FromRgb(0x25, 0x39, 0x4D)));
    private static readonly Brush PlainBackground = Frozen(new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2E)));
    private static readonly Geometry PinGeometry = Frozen(Geometry.Parse("M9,4 H15 L14,9 L17,12 H7 L10,9 Z M12,12 V20"));
    private static readonly Geometry CheckGeometry = Frozen(Geometry.Parse("M5,12 L10,17 L19,7"));
    private static readonly GameIconCache Icons = new();

    private readonly ShelfContext _context;
    private readonly GameNotesService _notes;
    private readonly UiSignal _signal;
    private readonly Brush _primary;
    private readonly Brush _secondary;
    private readonly Geometry _closeGeometry;
    private bool _subscribed;
    private bool _rendered;
    private bool _loadFailed;
    private string? _shownKey;
    private Guid? _pulseId;
    private int _switchToken;

    public GameNotesWidget(ShelfContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        InitializeComponent();
        _context = context;
        _notes = context.GameNotes ?? throw new ArgumentException("The shelf context has no game notes service.", nameof(context));
        _signal = new UiSignal(Dispatcher, Refresh);
        _primary = (Brush)FindResource("TextPrimaryBrush");
        _secondary = (Brush)FindResource("TextSecondaryBrush");
        _closeGeometry = (Geometry)FindResource("WidgetIcon.Close");

        PrevButton.Click += () => _notes.BrowseStep(-1);
        NextButton.Click += () => _notes.BrowseStep(1);
        PinButton.Click += () => _notes.TogglePinnedGame();
        CaptureButton.Click += () => _context.GameNotesHost?.OpenForCapture(_shownKey);
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }

    private async void Subscribe()
    {
        if (_subscribed) return;

        _subscribed = true;
        _notes.Changed += OnNotesChanged;
        if (_context.GameNotesHost is { } host) host.HotkeyConflictChanged += OnHotkeyConflictChanged;
        Refresh();

        if (_notes.IsInitialized) return;
        try
        {
            await _notes.InitializeAsync();
            _loadFailed = false;
        }
        catch (Exception)
        {
            // The footer explains it; the list stays empty instead of showing the wrong game's notes.
            _loadFailed = true;
            Refresh();
        }
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;

        _notes.Changed -= OnNotesChanged;
        if (_context.GameNotesHost is { } host) host.HotkeyConflictChanged -= OnHotkeyConflictChanged;
        _subscribed = false;
    }

    private void OnNotesChanged() => _signal.Signal();

    private void OnHotkeyConflictChanged(bool _) => _signal.Signal();

    private bool ReduceAnimations() => _context.Settings().ReduceAnimations;

    /// <summary>A change of game fades the list out and back in. Other changes redraw at once.</summary>
    private void Refresh()
    {
        string? key = _notes.ResolvedKey;
        if (_rendered && key != _shownKey && !ReduceAnimations())
        {
            _ = SwitchGameAsync();
            return;
        }
        Render();
    }

    private async Task SwitchGameAsync()
    {
        int token = ++_switchToken;
        await RunAsync(animation => NotesList.BeginAnimation(OpacityProperty, animation), 1, 0, SwitchOutMilliseconds);
        if (token != _switchToken) return;

        Render();
        _ = RunAsync(animation => NotesList.BeginAnimation(OpacityProperty, animation), 0, 1, SwitchInMilliseconds);
        await RunAsync(animation => ListOffset.BeginAnimation(TranslateTransform.YProperty, animation), 6, 0, SwitchInMilliseconds);
    }

    private void Render()
    {
        _rendered = true;
        string? key = _notes.ResolvedKey;
        _shownKey = key;

        // Clears the holds left by a fade, so the list is visible whatever state the last transition ended in.
        NotesList.BeginAnimation(OpacityProperty, null);
        NotesList.Opacity = 1;
        ListOffset.BeginAnimation(TranslateTransform.YProperty, null);
        ListOffset.Y = 0;

        IReadOnlyList<GameNote> notes = _notes.NotesFor(key);
        string? name = key is null ? null : _notes.DisplayNameFor(key);
        RenderHeader(key, name, notes.Count);
        RenderNotes(key, notes, name);
        RenderFooter();
        _pulseId = null;
    }

    private void RenderHeader(string? key, string? name, int noteCount)
    {
        GameNameText.Text = name ?? "Nenhum jogo";
        CountText.Text = GameNotesFormat.CountText(noteCount);

        ImageSource? icon = Icons.Get(_notes.ExePathFor(key));
        GameIcon.Source = icon;
        GameIcon.Visibility = icon is null ? Visibility.Collapsed : Visibility.Visible;
        GlyphBox.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed;

        bool canBrowse = NavigableKeys(key).Count > 1;
        PrevButton.Visibility = canBrowse ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = canBrowse ? Visibility.Visible : Visibility.Collapsed;

        bool hasGame = key is not null;
        bool pinned = _notes.IsPinned(key);
        PinButton.LabelText = pinned ? "Fixado" : "Fixar";
        PinButton.Background = pinned ? PinnedBackground : PlainBackground;
        PinButton.Opacity = hasGame ? 1 : 0.4;
        CaptureButton.Opacity = hasGame ? 1 : 0.4;
    }

    /// <summary>Games the arrows move through: the games with notes, plus the one on screen if it has none yet.</summary>
    private List<string> NavigableKeys(string? key)
    {
        var keys = _notes.GamesWithNotes.Select(game => game.Key).ToList();
        if (key is not null && !keys.Contains(key)) keys.Insert(0, key);
        return keys;
    }

    private void RenderNotes(string? key, IReadOnlyList<GameNote> notes, string? name)
    {
        NotesList.Children.Clear();

        bool empty = key is null || notes.Count == 0;
        EmptyText.Text = GameNotesFormat.EmptyText(name);
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty || key is null) return;

        int visible = notes.Count <= MaxRows ? notes.Count : MaxRows - 1;
        for (int i = 0; i < visible; i++)
        {
            NotesList.Children.Add(BuildRow(key, notes[i]));
        }

        if (visible < notes.Count)
        {
            NotesList.Children.Add(new TextBlock
            {
                Text = GameNotesFormat.OverflowText(notes.Count - visible),
                FontSize = 10,
                Foreground = _secondary,
                Margin = new Thickness(0, 1, 0, 0),
            });
        }
    }

    private FrameworkElement BuildRow(string key, GameNote note)
    {
        var row = new Grid
        {
            Height = 22,
            Margin = new Thickness(0, 0, 0, 2),
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new Border
        {
            Width = 13,
            Height = 13,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = note.IsCompleted ? BlueBrush : MutedBrush,
            Background = note.IsCompleted ? BlueBrush : Brushes.Transparent,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (note.IsCompleted)
        {
            box.Child = new Path
            {
                Data = CheckGeometry,
                Stroke = InkBrush,
                StrokeThickness = 1.8,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = 9,
                Height = 9,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        // Only the row that was just toggled animates; the rest appear in their final state.
        if (_pulseId == note.Id) AnimateFill(box, note.IsCompleted);
        Grid.SetColumn(box, 0);

        var text = new TextBlock
        {
            Text = note.Text,
            FontSize = 11.5,
            Foreground = note.IsCompleted ? _secondary : _primary,
            Opacity = note.IsCompleted ? 0.55 : 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            TextDecorations = note.IsCompleted ? System.Windows.TextDecorations.Strikethrough : null,
        };
        Grid.SetColumn(text, 1);

        var pinGlyph = new Path
        {
            Data = PinGeometry,
            Stroke = note.IsPinned ? YellowBrush : MutedBrush,
            StrokeThickness = 1.3,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 10,
            Height = 10,
            Stretch = Stretch.Uniform,
        };
        var pin = new Border
        {
            Width = 18,
            Height = 22,
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = note.IsPinned ? "Desafixar nota" : "Fixar nota",
            Child = pinGlyph,
        };
        pin.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _ = _notes.SetNotePinnedAsync(key, note.Id, !note.IsPinned);
        };
        Grid.SetColumn(pin, 2);

        var remove = new Border
        {
            Width = 18,
            Height = 22,
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Apagar nota",
            Child = new Path
            {
                Data = _closeGeometry,
                Stroke = MutedBrush,
                StrokeThickness = 1.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = 8,
                Height = 8,
                Stretch = Stretch.Uniform,
            },
        };
        remove.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _ = _notes.RemoveNoteAsync(key, note.Id);
        };
        Grid.SetColumn(remove, 3);

        row.MouseLeftButtonUp += (_, _) =>
        {
            _pulseId = note.Id;
            _ = _notes.ToggleNoteAsync(key, note.Id);
        };

        row.Children.Add(box);
        row.Children.Add(text);
        row.Children.Add(pin);
        row.Children.Add(remove);
        return row;
    }

    /// <summary>Fills or empties the checkbox with a short colour fade. Instant when animations are reduced.</summary>
    private void AnimateFill(Border box, bool completed)
    {
        if (ReduceAnimations()) return;

        Color from = completed ? Color.FromArgb(0, 0x65, 0xB6, 0xFF) : BlueColor;
        Color to = completed ? BlueColor : Color.FromArgb(0, 0x65, 0xB6, 0xFF);
        var fill = new SolidColorBrush(from);
        box.Background = fill;
        fill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(CheckMilliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void RenderFooter()
    {
        string? message = _loadFailed
            ? "Não foi possível ler as notas."
            : _notes.HasSaveError
                ? "Não foi possível salvar. Tente de novo."
                : _context.GameNotesHost?.HotkeyConflict == true
                    ? "Ctrl+Alt+G está em uso. Use + para anotar."
                    : null;

        FooterText.Text = message ?? string.Empty;
        FooterPanel.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Runs one double animation and completes when it ends. Starts it through <paramref name="begin"/>.</summary>
    private static Task RunAsync(Action<DoubleAnimation> begin, double from, double to, int milliseconds)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        animation.Completed += (_, _) => completion.TrySetResult();
        begin(animation);
        return completion.Task;
    }

    private static T Frozen<T>(T value) where T : Freezable
    {
        value.Freeze();
        return value;
    }
}
