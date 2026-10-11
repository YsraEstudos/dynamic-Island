using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Island.App.Widgets;
using Island.Core.Tools.TextAnalyzer;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using UserControl = System.Windows.Controls.UserControl;

namespace Island.App.Views.Widgets;

/// <summary>
/// Shelf entry that analyses the most recent copied text: the island cannot take typing, so the clipboard is the source.
/// A dropped .txt file or dropped text is analysed instead, until something new is copied.
/// </summary>
public partial class TextAnalyzerWidget : UserControl
{
    private const int StaggerMilliseconds = 35;
    private const double ProgressBarWidth = 70;

    // Widget width (320) less the 14 DIP margin on each side, minus the bar: how far the bar can travel.
    private const double ProgressTravel = 222;

    private static readonly Brush IdleStroke = Frozen(Color.FromRgb(0x30, 0x30, 0x33));
    private static readonly Brush HoverStroke = Frozen(Color.FromRgb(0x8E, 0x8E, 0x93));

    private readonly ShelfContext _context;
    private readonly TextAnalyzerModel _model;
    private readonly UiSignal _signal;
    private readonly UiSignal _clipboardSignal;
    private readonly TextAnalyzerCard[] _cards;
    private bool _subscribed;
    private bool _progressRunning;
    private string? _shownText;
    private string? _notice;

    public TextAnalyzerWidget(ShelfContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        InitializeComponent();
        _context = context;
        _model = context.Tools.GetOrCreate(() => new TextAnalyzerModel());
        _signal = new UiSignal(Dispatcher, Refresh);
        _clipboardSignal = new UiSignal(Dispatcher, SyncFromClipboard);
        _cards = [CharactersCard, WordsCard, ParagraphsCard, ReadingCard, TokensCard, SentencesCard];

        PasteButton.Click += () => Apply(TextAnalyzerModel.LatestClipboardText(_context.Clipboard.Items));

        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => SetDragHighlight(false);
        Drop += OnDrop;

        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }

    private void Subscribe()
    {
        if (_subscribed) return;

        _subscribed = true;
        _model.Changed += OnModelChanged;
        _context.Clipboard.Changed += OnClipboardChanged;
        // Catches up on a copy made while the widget was off screen.
        SyncFromClipboard();
        Refresh();
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;

        _subscribed = false;
        _model.Changed -= OnModelChanged;
        _context.Clipboard.Changed -= OnClipboardChanged;
        StopProgress();
    }

    /// <summary>Arbitrary thread.</summary>
    private void OnModelChanged() => _signal.Signal();

    /// <summary>Arbitrary thread.</summary>
    private void OnClipboardChanged() => _clipboardSignal.Signal();

    private bool ReduceAnimations() => _context.Settings().ReduceAnimations;

    /// <summary>Follows the newest copied text. Without a text on the clipboard, what is shown stays.</summary>
    private void SyncFromClipboard()
    {
        string? latest = TextAnalyzerModel.LatestClipboardText(_context.Clipboard.Items);
        if (latest is not null && !string.Equals(latest, _model.State.Text, StringComparison.Ordinal)) Apply(latest);
    }

    /// <summary>Analyses <paramref name="text"/>; null clears the widget. A new text replaces any earlier notice.</summary>
    private void Apply(string? text)
    {
        _notice = null;
        _model.SetText(text);
        Refresh();
    }

    private void Refresh()
    {
        TextAnalyzerModel.Snapshot state = _model.State;
        bool reduce = ReduceAnimations();
        bool hasText = state.Text is not null;
        bool textChanged = !string.Equals(state.Text, _shownText, StringComparison.Ordinal);
        _shownText = state.Text;

        EmptyText.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        CardsGrid.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        PreviewText.Text = _notice ?? TextStatsFormat.Preview(state.Text);
        SetProgress(state.IsAnalyzing, reduce);
        if (!hasText) return;

        ShowStats(state.Stats, !reduce);

        if (!textChanged) return;
        for (int i = 0; i < _cards.Length; i++)
        {
            _cards[i].Reveal(!reduce, TimeSpan.FromMilliseconds(i * StaggerMilliseconds));
        }
    }

    private void ShowStats(TextStats stats, bool animate)
    {
        CharactersCard.Show(stats.Characters, Count, animate);
        WordsCard.Show(stats.Words, Count, animate);
        ParagraphsCard.Show(stats.Paragraphs, Count, animate);
        ReadingCard.Show(stats.ReadingTime.TotalSeconds, Clock, animate);
        TokensCard.Show(stats.EstimatedTokens, Tokens, animate);
        SentencesCard.Show(stats.Sentences, Count, animate);

        CharactersCard.ToolTip = $"Sem espaços: {TextStatsFormat.Number(stats.CharactersNoSpaces)}";
        ReadingCard.ToolTip = $"Leitura silenciosa a {TextAnalyzer.ReadingWordsPerMinute} palavras por minuto.\n"
            + $"Fala a {TextAnalyzer.SpeakingWordsPerMinute} palavras por minuto: {TextStatsFormat.Duration(stats.SpeakingTime)}";
    }

    private static string Count(double value) => TextStatsFormat.Number((long)Math.Round(value));

    private static string Clock(double seconds) => TextStatsFormat.Duration(TimeSpan.FromSeconds(Math.Round(seconds)));

    private static string Tokens(double value) => TextStatsFormat.Tokens((int)Math.Round(value));

    /// <summary>The thin bar runs only while a text is being analysed. Reduced animations show it still, full width.</summary>
    private void SetProgress(bool analyzing, bool reduce)
    {
        ProgressTrack.Visibility = analyzing ? Visibility.Visible : Visibility.Collapsed;
        if (!analyzing)
        {
            StopProgress();
            return;
        }

        if (_progressRunning) return;
        _progressRunning = true;

        if (reduce)
        {
            ProgressBar.HorizontalAlignment = HorizontalAlignment.Stretch;
            ProgressBar.Width = double.NaN;
            return;
        }

        ProgressBar.HorizontalAlignment = HorizontalAlignment.Left;
        ProgressBar.Width = ProgressBarWidth;
        ProgressOffset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, ProgressTravel, TimeSpan.FromMilliseconds(900))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    private void StopProgress()
    {
        ProgressOffset.BeginAnimation(TranslateTransform.XProperty, null);
        ProgressOffset.X = 0;
        _progressRunning = false;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool accepts = DroppedTextFile(e.Data) is not null || e.Data.GetDataPresent(DataFormats.UnicodeText);
        e.Effects = accepts ? DragDropEffects.Copy : DragDropEffects.None;
        SetDragHighlight(accepts);
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        SetDragHighlight(false);
        if (DroppedTextFile(e.Data) is { } file)
        {
            _ = LoadFileAsync(file);
        }
        else if (e.Data.GetData(DataFormats.UnicodeText) is string text)
        {
            Apply(text);
        }
        e.Handled = true;
    }

    /// <summary>Reads a dropped .txt file and analyses it. A file that cannot be read says so instead.</summary>
    private async Task LoadFileAsync(string path)
    {
        try
        {
            string text = await File.ReadAllTextAsync(path);
            Apply(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notice = "Não foi possível ler o arquivo.";
            Refresh();
        }
    }

    private static string? DroppedTextFile(System.Windows.IDataObject data) =>
        data.GetData(DataFormats.FileDrop) is string[] paths
            ? Array.Find(paths, IsTextFile)
            : null;

    private static bool IsTextFile(string path) =>
        string.Equals(System.IO.Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase);

    private void SetDragHighlight(bool on) => Surface.BorderBrush = on ? HoverStroke : IdleStroke;

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
