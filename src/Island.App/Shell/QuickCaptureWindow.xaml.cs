using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Island.App.Animations;
using Island.App.Notes;
using Island.Core.Notes;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;

namespace Island.App.Shell;

/// <summary>
/// Small activatable window for jotting a note without opening the whole app. It grows out of the widget's capture bar
/// (the island itself never takes the keyboard). Enter saves, Shift+Enter breaks the line, Ctrl+Enter continues in the app.
/// </summary>
public partial class QuickCaptureWindow : Window
{
    private const double Margin = 10;
    private const double CardWidth = 400;
    private const double CardHeight = 206;
    private static readonly Color WidgetColor = Color.FromRgb(0x16, 0x16, 0x16);
    private static readonly Color CardColor = Color.FromRgb(0x1B, 0x1B, 0x1E);

    private readonly QuickNotesService _service;
    private readonly Func<bool> _reduceAnimations;
    private readonly Action<string, Func<Rect?>, Func<Rect?>?> _openInApp;
    private readonly WindowMorph _morph;
    private readonly Dictionary<QuickNoteColor, Border> _dots = [];
    private Func<Rect?>? _source;
    private QuickNoteColor _color = QuickNoteColor.Default;
    private bool _pinned;
    private bool _busy;
    private bool _closing;
    private bool _allowClose;
    private bool _discardArmed;

    public QuickCaptureWindow(
        QuickNotesService service,
        Func<bool> reduceAnimations,
        Action<string, Func<Rect?>, Func<Rect?>?> openInApp)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _reduceAnimations = reduceAnimations ?? throw new ArgumentNullException(nameof(reduceAnimations));
        _openInApp = openInApp ?? throw new ArgumentNullException(nameof(openInApp));
        InitializeComponent();

        _morph = new WindowMorph(Card, Content, Shadow, WidgetColor, CardColor);
        BuildColorDots();
        NoteBox.TextChanged += (_, _) =>
        {
            Placeholder.Visibility = string.IsNullOrEmpty(NoteBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(NoteBox.Text);
            if (_discardArmed) ResetHint();
        };
        SaveButton.IsEnabled = false;
        Deactivated += OnDeactivated;
    }

    /// <summary>Shows (or re-focuses) the window, growing out of <paramref name="source"/> when given.</summary>
    public void ShowCapture(Func<Rect?>? source)
    {
        if (IsVisible && !_closing)
        {
            Activate();
            NoteBox.Focus();
            return;
        }

        _closing = false;
        _source = source;
        _color = QuickNoteColor.Default;
        _pinned = false;
        NoteBox.Clear();
        UpdateDots();
        UpdatePinGlyph();
        ResetHint();

        Rect? from = source?.Invoke();
        Rect card = ComputeCardRect(from);
        Left = card.X - Margin;
        Top = card.Y - Margin;
        Width = CardWidth + Margin * 2;
        Height = CardHeight + Margin * 2;

        Show();
        _morph.Open(card, from, _reduceAnimations());
        Activate();
        NoteBox.Focus();
        Keyboard.Focus(NoteBox);
    }

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    private static Rect ComputeCardRect(Rect? from)
    {
        Rect area = WindowPlacement.WorkArea;
        double x;
        double y;
        if (from is { } source)
        {
            x = source.X + (source.Width - CardWidth) / 2;
            y = source.Y;
        }
        else
        {
            x = area.Left + (area.Width - CardWidth) / 2;
            y = area.Top + 96;
        }

        return WindowPlacement.Clamp(new Rect(x, y, CardWidth, CardHeight), area);
    }

    private Rect CurrentCardRect() => new(Left + Margin, Top + Margin, CardWidth, CardHeight);

    private void BuildColorDots()
    {
        foreach (QuickNoteColor color in Enum.GetValues<QuickNoteColor>())
        {
            var dot = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 7, 0),
                Background = QuickNoteDisplay.Accent(color),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = QuickNoteDisplay.ColorName(color),
            };
            System.Windows.Automation.AutomationProperties.SetName(dot, "Cor " + QuickNoteDisplay.ColorName(color));
            QuickNoteColor captured = color;
            dot.MouseLeftButtonUp += (_, _) =>
            {
                _color = captured;
                UpdateDots();
                NoteBox.Focus();
            };
            _dots[color] = dot;
            ColorDots.Children.Add(dot);
        }

        UpdateDots();
    }

    private void UpdateDots()
    {
        foreach ((QuickNoteColor color, Border dot) in _dots)
            dot.BorderBrush = color == _color ? Brushes.White : Brushes.Transparent;
    }

    private void UpdatePinGlyph()
    {
        PinGlyph.Text = _pinned ? "★" : "☆";
        PinGlyph.Foreground = _pinned ? new SolidColorBrush(Color.FromRgb(0xF5, 0xC4, 0x51)) : new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xA6));
    }

    private void ResetHint()
    {
        _discardArmed = false;
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x73));
        StatusText.Text = "Enter salva  ·  Ctrl+Enter abre no app";
    }

    private void ShowProblem(string message)
    {
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD4, 0xD7));
        StatusText.Text = message;
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        _pinned = !_pinned;
        UpdatePinGlyph();
        NoteBox.Focus();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => _ = SaveAsync();

    private void OnCloseClick(object sender, RoutedEventArgs e) => _ = RequestCloseAsync(force: false);

    private void OnOpenInAppClick(object sender, RoutedEventArgs e) => OpenInApp();

    private void OnNoteKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            OpenInApp();
        }
        else if (Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            _ = SaveAsync();
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        e.Handled = true;
        _ = RequestCloseAsync(force: false);
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Clicking away dismisses an empty capture; typed text is never thrown away silently.
        if (!_busy && !_closing && string.IsNullOrWhiteSpace(NoteBox.Text)) _ = RequestCloseAsync(force: true);
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || _closing) return;

        e.Cancel = true;
        _ = RequestCloseAsync(force: true);
    }

    private async Task SaveAsync()
    {
        string text = NoteBox.Text;
        if (_busy || _closing || string.IsNullOrWhiteSpace(text)) return;

        _busy = true;
        SaveButton.IsEnabled = false;
        try
        {
            QuickNote? saved = await _service.CaptureAsync(text, _color, _pinned);
            if (saved is null)
            {
                ShowProblem("Não foi possível salvar. Tente de novo.");
                return;
            }

            NoteBox.Clear();
            await RequestCloseAsync(force: true);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Quick capture could not save the note");
            ShowProblem("Não foi possível salvar. Tente de novo.");
        }
        finally
        {
            _busy = false;
            SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(NoteBox.Text);
        }
    }

    private void OpenInApp()
    {
        if (_busy || _closing) return;

        string text = NoteBox.Text;
        Rect card = CurrentCardRect();
        _closing = true;
        _openInApp(text, () => card, _source);

        NoteBox.Clear();
        _allowClose = true;
        Close();
    }

    private async Task RequestCloseAsync(bool force)
    {
        if (_closing) return;

        // Esc on text: the first press only warns, so a stray key never loses a thought.
        if (!force && !string.IsNullOrWhiteSpace(NoteBox.Text) && !_discardArmed)
        {
            _discardArmed = true;
            ShowProblem("Esc de novo para descartar, ou Enter para salvar");
            return;
        }

        _closing = true;
        Rect? target = _source?.Invoke();
        await _morph.CloseAsync(CurrentCardRect(), target, _reduceAnimations());
        _allowClose = true;
        Close();
    }
}
