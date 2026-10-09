using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Island.App.Notes;
using Island.App.Shell;
using Island.App.Widgets;
using Island.Core.Notes;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using UserControl = System.Windows.Controls.UserControl;
using Cursors = System.Windows.Input.Cursors;

namespace Island.App.Views.Widgets;

/// <summary>
/// Compact shelf entry point: a capture bar (opens the small capture window), the two latest notes (click to open one in
/// the app) and "Abrir app". Both windows grow out of this widget. The island never takes the keyboard, so typing happens there.
/// </summary>
public partial class QuickNotesWidget : UserControl
{
    private const int VisibleNotes = 2;

    private readonly ShelfContext _context;
    private readonly UiSignal _signal;
    private bool _subscribed;

    public QuickNotesWidget(ShelfContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        InitializeComponent();
        _context = context;
        _signal = new UiSignal(Dispatcher, Refresh);

        OpenButton.Click += () => _context.QuickNotesHost.OpenNotes(WidgetBounds);
        CaptureButton.Click += () => _context.QuickNotesHost.OpenForCapture(CaptureBounds);
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }

    private Rect? WidgetBounds() => WindowPlacement.ScreenBounds(Face);

    private Rect? CaptureBounds() => WindowPlacement.ScreenBounds(CaptureButton);

    private void Subscribe()
    {
        if (_subscribed) return;

        _context.QuickNotes.Changed += OnNotesChanged;
        _context.QuickNotesHost.HotkeyConflictChanged += OnHotkeyConflictChanged;
        _subscribed = true;
        Refresh();
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;

        _context.QuickNotes.Changed -= OnNotesChanged;
        _context.QuickNotesHost.HotkeyConflictChanged -= OnHotkeyConflictChanged;
        _subscribed = false;
    }

    private void OnNotesChanged() => _signal.Signal();

    private void OnHotkeyConflictChanged(bool _) => _signal.Signal();

    private void Refresh()
    {
        IReadOnlyList<QuickNote> allNotes = _context.QuickNotes.GetNotes();
        CountText.Text = allNotes.Count == 0 ? string.Empty : allNotes.Count.ToString();
        NotesList.Children.Clear();
        foreach (QuickNote note in allNotes.Take(VisibleNotes))
            NotesList.Children.Add(BuildRow(note));

        bool hasNotes = allNotes.Count > 0;
        NotesList.Visibility = hasNotes ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = hasNotes ? Visibility.Collapsed : Visibility.Visible;
        HotkeyConflictPanel.Visibility = _context.Settings().QuickNotesHotkeyEnabled
            && _context.QuickNotesHost.HotkeyConflict
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private FrameworkElement BuildRow(QuickNote note)
    {
        string title = QuickNoteDisplay.Title(note);
        string preview = QuickNoteDisplay.Preview(note);

        var line = new TextBlock
        {
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        line.Inlines.Add(new System.Windows.Documents.Run(title)
        {
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        });
        if (preview.Length > 0)
        {
            line.Inlines.Add(new System.Windows.Documents.Run("   " + preview)
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93)),
            });
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var bar = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Background = QuickNoteDisplay.Accent(note.Color),
            Margin = new Thickness(0, 1, 8, 1),
        };
        grid.Children.Add(bar);
        Grid.SetColumn(line, 1);
        grid.Children.Add(line);

        if (note.IsPinned)
        {
            var star = new TextBlock
            {
                Text = "★",
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xC4, 0x51)),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(star, 2);
            grid.Children.Add(star);
        }

        var idle = Brushes.Transparent;
        var hover = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x27));
        var row = new Border
        {
            Background = idle,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 3, 7, 3),
            Margin = new Thickness(0, 0, 0, 2),
            Height = 24,
            Cursor = Cursors.Hand,
            Child = grid,
            ToolTip = preview.Length > 0 ? title + "\n" + preview : title,
        };
        AutomationProperties.SetName(row, "Abrir nota: " + title);
        row.MouseEnter += (_, _) => row.Background = hover;
        row.MouseLeave += (_, _) => row.Background = idle;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _context.QuickNotesHost.OpenNote(note.Id, WidgetBounds);
        };
        return row;
    }
}
