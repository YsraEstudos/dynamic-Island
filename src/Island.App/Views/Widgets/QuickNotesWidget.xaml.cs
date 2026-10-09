using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Island.App.Widgets;
using Island.Core.Notes;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using UserControl = System.Windows.Controls.UserControl;

namespace Island.App.Views.Widgets;

/// <summary>Compact shelf entry point with two live note previews and a one-click capture action.</summary>
public partial class QuickNotesWidget : UserControl
{
    private static readonly Brush DefaultAccent = FrozenColor(0x68, 0x70, 0x7E);
    private static readonly Brush BlueAccent = FrozenColor(0x65, 0xB6, 0xFF);
    private static readonly Brush GreenAccent = FrozenColor(0x69, 0xC9, 0x8E);
    private static readonly Brush YellowAccent = FrozenColor(0xF5, 0xC4, 0x51);
    private static readonly Brush PinkAccent = FrozenColor(0xF0, 0x8A, 0xB5);
    private static readonly Brush PurpleAccent = FrozenColor(0xBB, 0x9A, 0xF7);

    private readonly ShelfContext _context;
    private readonly UiSignal _signal;
    private bool _subscribed;

    public QuickNotesWidget(ShelfContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        InitializeComponent();
        _context = context;
        _signal = new UiSignal(Dispatcher, Refresh);

        OpenButton.Click += _context.QuickNotesHost.OpenNotes;
        CaptureButton.Click += _context.QuickNotesHost.OpenForCapture;
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }

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
        IReadOnlyList<QuickNote> notes = allNotes.Take(2).ToArray();
        CountText.Text = allNotes.Count == 0 ? string.Empty : allNotes.Count.ToString();
        NotesList.Children.Clear();
        foreach (QuickNote note in notes)
            NotesList.Children.Add(BuildPreview(note));

        bool hasNotes = notes.Count > 0;
        NotesList.Visibility = hasNotes ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = hasNotes ? Visibility.Collapsed : Visibility.Visible;
        HotkeyConflictPanel.Visibility = _context.Settings().QuickNotesHotkeyEnabled
            && _context.QuickNotesHost.HotkeyConflict
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static FrameworkElement BuildPreview(QuickNote note)
    {
        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(note.Title) ? "Sem título" : note.Title,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var content = new TextBlock
        {
            Text = PreviewText(note),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xA6)),
            Margin = new Thickness(0, 2, 0, 0),
            MaxHeight = 16,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(title);
        stack.Children.Add(content);

        var preview = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x22)),
            BorderBrush = AccentFor(note.Color),
            BorderThickness = new Thickness(2, 1, 1, 1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 0, 5),
            Child = stack,
        };
        AutomationProperties.SetName(preview, PreviewText(note));
        return preview;
    }

    private static Brush AccentFor(QuickNoteColor color) => color switch
    {
        QuickNoteColor.Blue => BlueAccent,
        QuickNoteColor.Green => GreenAccent,
        QuickNoteColor.Yellow => YellowAccent,
        QuickNoteColor.Pink => PinkAccent,
        QuickNoteColor.Purple => PurpleAccent,
        _ => DefaultAccent
    };

    private static Brush FrozenColor(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static string PreviewText(QuickNote note)
    {
        if (!string.IsNullOrWhiteSpace(note.Content)) return note.Content;
        if (note.Checklist.Count > 0) return $"☐ {note.Checklist[0].Text}";
        return note.Tags.Count > 0 ? string.Join(" · ", note.Tags) : "Toque em Abrir para editar";
    }
}
