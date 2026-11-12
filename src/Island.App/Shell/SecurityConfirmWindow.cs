using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Island.Core.Pomodoro;
// The WinForms implicit usings are active in this project (UseWindowsForms), so the WPF types need explicit aliases.
using Button = System.Windows.Controls.Button;
using DataObject = System.Windows.DataObject;
using FontFamily = System.Windows.Media.FontFamily;
using FontWeights = System.Windows.FontWeights;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using Thickness = System.Windows.Thickness;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Island.App.Shell;

/// <summary>
/// Friction for weakening a security setting outside an Angry session (the phone block toggle). The user must type a
/// long phrase from <see cref="UnlockPhrases"/>, so a stray click or a script cannot quietly turn the protection off;
/// paste is refused, as in the Angry unlock window. A weak confirmation is better than none, and this dialog is only a
/// second line: during a session the setting is locked outright and a live state restore re-applies it after a kill.
/// </summary>
public sealed class SecurityConfirmWindow : Window
{
    private readonly string _phrase = UnlockPhrases.Pick();
    private readonly TextBox _input;
    private readonly TextBlock _feedback;

    private SecurityConfirmWindow(string action)
    {
        Title = "Dynamic Island — confirmação de segurança";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1D, 0x22));
        Foreground = Brushes.WhiteSmoke;

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = $"Você está prestes a {action}.",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(new TextBlock
        {
            Text = "Isso reduz a proteção. Seu foco está em jogo: digite a frase abaixo para confirmar.",
            Margin = new Thickness(0, 8, 0, 12),
            TextWrapping = TextWrapping.Wrap,
        });

        var phraseBlock = new TextBlock
        {
            Text = _phrase,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(12),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x33)),
        };
        root.Children.Add(phraseBlock);

        _input = new TextBox
        {
            Margin = new Thickness(0, 12, 0, 0),
            FontSize = 13,
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x14, 0x18)),
            Foreground = Brushes.WhiteSmoke,
            CaretBrush = Brushes.WhiteSmoke,
            AcceptsReturn = false,
        };
        // Typing the phrase is the point: paste and drag would turn the confirmation into a two-second step.
        DataObject.AddPastingHandler(_input, (_, e) => e.CancelCommand());
        _input.AllowDrop = false;
        root.Children.Add(_input);

        _feedback = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x9C, 0x9C)),
            TextWrapping = TextWrapping.Wrap,
        };
        root.Children.Add(_feedback);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var cancel = new Button { Content = "Voltar", MinWidth = 90, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => { DialogResult = false; };
        var confirm = new Button { Content = "Desligar mesmo assim", MinWidth = 150, Padding = new Thickness(12, 6, 12, 6) };
        confirm.Click += (_, _) =>
        {
            if (UnlockPhrases.IsMatch(_phrase, _input.Text))
            {
                DialogResult = true;
                return;
            }
            _feedback.Text = "A frase não confere. Digite exatamente o que está acima.";
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => _input.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) DialogResult = false;
        };
    }

    /// <summary>
    /// Asks for the typed-phrase confirmation. Returns true only when the user typed the phrase correctly. Safe to call
    /// from the settings window (UI thread).
    /// </summary>
    public static bool Confirm(Window? owner, string action)
    {
        var window = new SecurityConfirmWindow(action);
        if (owner is { IsVisible: true }) window.Owner = owner;
        return window.ShowDialog() == true;
    }
}
