using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Island.Core.Pomodoro;
using Brush = System.Windows.Media.Brush;

namespace Island.App.Shell;

/// <summary>
/// "Desistir do foco?" screen of the Angry Pomodoro. The lock is released only by typing the shown phrase: paste,
/// drag, undo and bulk insertion are refused, while normal typing (accents and dead keys included) works.
/// Esc or "Voltar ao foco" keeps the session. Open it with <see cref="ShowFor"/>.
/// </summary>
public partial class UnlockWindow : Window
{
    private static UnlockWindow? _open;

    private readonly AngryPomodoro _angry;
    private readonly string _phrase;
    private string _lastAccepted = string.Empty;
    private bool _reverting;
    private bool _closed;

    public UnlockWindow(AngryPomodoro angry)
    {
        ArgumentNullException.ThrowIfNull(angry);

        InitializeComponent();
        _angry = angry;
        _phrase = angry.NewPhrase();
        PhraseText.Text = _phrase;

        // Paste is refused at the command, at the paste event, and by the keyboard shortcuts below.
        CommandManager.AddPreviewCanExecuteHandler(this, (_, e) =>
        {
            if (e.Command == ApplicationCommands.Paste)
            {
                e.CanExecute = false;
                e.Handled = true;
            }
        });
        System.Windows.DataObject.AddPastingHandler(InputBox, (_, e) => e.CancelCommand());

        InputBox.PreviewKeyDown += (_, e) =>
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            Key key = e.Key;

            // Enter would insert a line break, which the phrase never contains. Ctrl+V/Z/Y and Shift+Insert paste or undo.
            if (key == Key.Enter
                || (ctrl && (key == Key.V || key == Key.Z || key == Key.Y))
                || (shift && key == Key.Insert))
            {
                e.Handled = true;
            }
        };
        InputBox.PreviewTextInput += (_, e) =>
        {
            // A keystroke produces one character; longer text is bulk insertion (automation, text expanders).
            if (e.Text.Length != 1)
            {
                e.Handled = true;
            }
        };
        InputBox.TextChanged += OnInputTextChanged;

        InputBox.ContextMenuOpening += (_, e) => e.Handled = true;
        InputBox.PreviewDragEnter += (_, e) => e.Handled = true;
        InputBox.PreviewDragOver += (_, e) => e.Handled = true;
        InputBox.PreviewDrop += (_, e) => e.Handled = true;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                DialogResult = false;
            }
        };

        angry.LockChanged += OnLockChanged;
        Closed += OnClosed;
        UpdateFeedback();
    }

    /// <summary>
    /// Shows the dialog modally. Returns true when the lock is gone (phrase typed, or the focus timer ended meanwhile),
    /// false when the user keeps the session. Returns true at once when no lock is active. When the dialog is already
    /// open it is activated and false is returned.
    /// </summary>
    public static bool ShowFor(AngryPomodoro angry)
    {
        ArgumentNullException.ThrowIfNull(angry);

        if (!angry.IsLocked)
        {
            return true;
        }

        if (_open is not null)
        {
            _open.Activate();
            return false;
        }

        var window = new UnlockWindow(angry);
        _open = window;
        bool? result;
        try
        {
            result = window.ShowDialog();
        }
        finally
        {
            _open = null;
        }

        return result == true || !angry.IsLocked;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Dark title bar, like the other app windows (DWMWA_USE_IMMERSIVE_DARK_MODE = 20, Windows 10 2004+).
        int useDark = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref useDark, sizeof(int));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
        InputBox.Focus();
    }

    private void OnStayClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnGiveUpClicked(object sender, RoutedEventArgs e)
    {
        // TryUnlock also returns true when the lock has already ended.
        if (_angry.TryUnlock(InputBox.Text))
        {
            DialogResult = true;
        }
    }

    // Raised on an arbitrary thread each time IsLocked flips; the close happens on the UI thread.
    private void OnLockChanged()
    {
        if (!_angry.IsLocked)
        {
            Dispatcher.BeginInvoke(new Action(CloseWhenUnlocked));
        }
    }

    private void CloseWhenUnlocked()
    {
        if (!_closed && !_angry.IsLocked)
        {
            DialogResult = true;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _angry.LockChanged -= OnLockChanged;
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_reverting)
        {
            return;
        }

        string typed = InputBox.Text;
        if (!UnlockPhrases.IsPlausibleKeystroke(_lastAccepted, typed))
        {
            // Not a keystroke (a paste-sized jump got through): restore the last accepted text, caret at the end.
            _reverting = true;
            try
            {
                InputBox.Text = _lastAccepted;
                InputBox.CaretIndex = _lastAccepted.Length;
            }
            finally
            {
                _reverting = false;
            }

            return;
        }

        _lastAccepted = typed;
        UpdateFeedback();
    }

    private void UpdateFeedback()
    {
        string typed = _lastAccepted;
        bool complete = UnlockPhrases.IsMatch(_phrase, typed);
        int matched = UnlockPhrases.CommonPrefixLength(_phrase, typed);

        // The matcher compares accent-free text. CommonPrefixLength(typed, typed) is the length of that folded text,
        // so the typed text is off track exactly when the match stops short of it.
        bool offTrack = matched < UnlockPhrases.CommonPrefixLength(typed, typed);

        ProgressText.Text = $"{matched} / {_phrase.Length}";
        GiveUpButton.IsEnabled = complete;

        string brushKey = complete ? "AccentGreenBrush" : offTrack ? "MutedRedBrush" : "MenuSeparatorBrush";
        InputBorder.BorderBrush = (Brush)FindResource(brushKey);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
