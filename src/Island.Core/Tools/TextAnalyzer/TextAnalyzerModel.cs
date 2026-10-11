using Island.Core.Clipboard;

namespace Island.Core.Tools.TextAnalyzer;

/// <summary>
/// The text the Analisador shows and its statistics. Lives in the <see cref="ToolRegistry"/>, so the result survives
/// the widget being rebuilt. Short texts are analysed at once; long ones on a worker thread, and a newer text cancels
/// the analysis still running. <see cref="Changed"/> is raised on whichever thread finished the work.
/// </summary>
public sealed class TextAnalyzerModel : IDisposable
{
    /// <summary>Texts up to this length are analysed on the caller's thread, where a worker hand-off would cost more.</summary>
    public const int SyncLimit = 20_000;

    private readonly object _gate = new();
    private string? _text;
    private TextStats _stats = TextStats.Empty;
    private CancellationTokenSource? _pending;
    private bool _disposed;

    /// <summary>Raised after the state changes. Arbitrary thread.</summary>
    public event Action? Changed;

    /// <summary>One consistent reading of the state, taken under the lock.</summary>
    public Snapshot State
    {
        get
        {
            lock (_gate) return new Snapshot(_text, _stats, _pending is not null);
        }
    }

    /// <summary>Analyses <paramref name="text"/> and shows it. Blank text clears the analysis.</summary>
    public void SetText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Apply(null, TextStats.Empty);
            return;
        }

        lock (_gate)
        {
            if (_pending is null && string.Equals(_text, text, StringComparison.Ordinal)) return;
        }

        if (text.Length <= SyncLimit)
        {
            Apply(text, TextAnalyzer.Analyze(text));
            return;
        }

        var source = new CancellationTokenSource();
        lock (_gate)
        {
            if (_disposed) return;
            _pending?.Cancel();
            _pending = source;
        }

        Changed?.Invoke();
        _ = Task.Run(() => Finish(text, source));
    }

    /// <summary>The most recent clipboard entry that holds text (images are skipped). Null when there is none.</summary>
    public static string? LatestClipboardText(IEnumerable<ClipboardItem> items)
    {
        foreach (ClipboardItem item in items)
        {
            if (item.Kind != ClipboardKind.Image && !string.IsNullOrWhiteSpace(item.Text)) return item.Text;
        }
        return null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending?.Cancel();
            _pending = null;
        }
    }

    private void Finish(string text, CancellationTokenSource source)
    {
        TextStats stats = TextAnalyzer.Analyze(text);

        bool published;
        lock (_gate)
        {
            // A superseded analysis is dropped: its source was cancelled when a newer text arrived.
            published = !_disposed && !source.IsCancellationRequested;
            if (published)
            {
                _text = text;
                _stats = stats;
                _pending = null;
            }
        }

        if (published) Changed?.Invoke();
    }

    private void Apply(string? text, TextStats stats)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _pending?.Cancel();
            _pending = null;
            _text = text;
            _stats = stats;
        }

        Changed?.Invoke();
    }

    /// <summary>What the widget draws: the text on screen, its counts, and whether a newer text is being analysed.</summary>
    public sealed record Snapshot(string? Text, TextStats Stats, bool IsAnalyzing);
}
