using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Island.Core.GameNotes;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Island.App.Shell;

/// <summary>Small activatable window that captures a note for one game. Enter adds the note; Esc hides the window.</summary>
public partial class GameNotesWindow : Window
{
    private readonly GameNotesService _service;
    private readonly Func<bool> _reduceAnimations;
    private string? _gameKey;
    private bool _adding;
    private bool _allowClose;

    public GameNotesWindow(GameNotesService service, Func<bool> reduceAnimations)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _reduceAnimations = reduceAnimations ?? throw new ArgumentNullException(nameof(reduceAnimations));
        InitializeComponent();
        NoteBox.TextChanged += (_, _) => Placeholder.Visibility = string.IsNullOrEmpty(NoteBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>Shows the window for a game. A null key (no game known yet) keeps the box disabled.</summary>
    public void ShowForGame(string? gameKey, string gameName)
    {
        _gameKey = gameKey;
        GameText.Text = gameKey is null ? GameNotesFormat.EmptyText(null) : $"Nota para {gameName}";
        NoteBox.IsEnabled = gameKey is not null;
        StatusText.Visibility = Visibility.Collapsed;

        if (!IsVisible)
        {
            if (!_reduceAnimations())
            {
                Opacity = 0;
                BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
            }
            Show();
        }

        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        NoteBox.Focus();
        Keyboard.Focus(NoteBox);
    }

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // The window is kept for the next capture; only a shutdown really closes it.
        if (_allowClose) return;

        e.Cancel = true;
        Hide();
    }

    private async void OnNoteKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Hide();
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            await AddNoteAsync();
        }
    }

    private async Task AddNoteAsync()
    {
        string? key = _gameKey;
        string text = NoteBox.Text;
        if (_adding || key is null || string.IsNullOrWhiteSpace(text)) return;

        _adding = true;
        try
        {
            bool saved = await _service.AddNoteAsync(key, text);
            if (saved)
            {
                NoteBox.Clear();
                StatusText.Visibility = Visibility.Collapsed;
            }
            else
            {
                StatusText.Text = _service.HasSaveError
                    ? "Não foi possível salvar a nota. Tente de novo."
                    : "Este jogo já tem o máximo de notas.";
                StatusText.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            _adding = false;
        }
    }
}
