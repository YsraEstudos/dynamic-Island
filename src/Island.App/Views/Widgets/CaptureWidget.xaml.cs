using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Island.App.Widgets;
using Island.Core.Abstractions;
using Island.Core.Capture;
using Island.Core.Clipboard;
using Island.Core.Configuration;
using Image = System.Windows.Controls.Image;
using UserControl = System.Windows.Controls.UserControl;

namespace Island.App.Views.Widgets;

/// <summary>
/// Shelf card for screen capture: takes a screenshot of the monitor under the foreground window, starts and stops a
/// recording (red dot with a slow pulse and a mm:ss timer), shows the latest screenshot and copies it to the clipboard.
/// The timer runs only while recording, so the idle shelf does no periodic work.
/// </summary>
public partial class CaptureWidget : UserControl
{
    private static readonly TimeSpan PulseDuration = TimeSpan.FromSeconds(0.9);
    private static readonly TimeSpan CopiedFeedback = TimeSpan.FromSeconds(1.2);
    private const int ThumbnailDecodeWidth = 192;

    private readonly CaptureController _capture;
    private readonly ICaptureShortcutStatus? _shortcuts;
    private readonly IClipboardService _clipboard;
    private readonly Func<IslandSettings> _settings;
    private readonly UiSignal _signal;
    private readonly DispatcherTimer _elapsedTimer;
    private readonly DispatcherTimer _copiedTimer;
    private readonly CaptureThumbnailMotion _thumbnailMotion;
    private bool _subscribed;
    private bool _shownOnce;
    private string? _shownThumbnail;
    private bool _pulsing;

    public CaptureWidget(ShelfContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        InitializeComponent();
        _capture = context.Capture ?? throw new InvalidOperationException("Capture controller is not available.");
        _shortcuts = context.CaptureShortcuts;
        _clipboard = context.ClipboardService;
        _settings = context.Settings;
        _signal = new UiSignal(Dispatcher, Refresh);
        _thumbnailMotion = new CaptureThumbnailMotion(ThumbFrame, ThumbScale);

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => UpdateTimer();
        _copiedTimer = new DispatcherTimer { Interval = CopiedFeedback };
        _copiedTimer.Tick += (_, _) =>
        {
            _copiedTimer.Stop();
            CopyButton.LabelText = "Copiar";
        };

        PrintButton.Click += () => _ = _capture.TakeScreenshotAsync();
        RecordButton.Click += () => _ = _capture.ToggleRecordingAsync();
        CopyButton.Click += CopyLatest;
        ThumbFrame.MouseLeftButtonUp += (_, _) => OpenLatest();

        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }

    private void Subscribe()
    {
        if (_subscribed) return;

        _capture.Changed += OnChanged;
        if (_shortcuts is not null) _shortcuts.AvailabilityChanged += OnChanged;
        _subscribed = true;
        Refresh();
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;

        _capture.Changed -= OnChanged;
        if (_shortcuts is not null) _shortcuts.AvailabilityChanged -= OnChanged;
        _subscribed = false;
        _elapsedTimer.Stop();
        _copiedTimer.Stop();
        StopPulse();
    }

    private void OnChanged() => _signal.Signal();

    private void Refresh()
    {
        RecordingPhase phase = _capture.Phase;
        bool recording = phase == RecordingPhase.Recording;
        bool reduce = _settings().ReduceAnimations;

        StatusTitle.Text = phase switch
        {
            RecordingPhase.Recording => "Gravando",
            RecordingPhase.Starting => "Iniciando",
            RecordingPhase.Stopping => "Salvando vídeo",
            _ => "Captura de tela",
        };
        StatusDetail.Text = recording ? "Pare com Ctrl+Alt+R ou no botão" : "Print com Ctrl+Alt+P";
        RecordDot.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        TimerText.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        RecordButton.LabelText = recording ? "Parar" : "Gravar";
        RecordButton.IsEnabled = phase is RecordingPhase.Idle or RecordingPhase.Recording;

        UpdateTimer();
        if (recording)
        {
            _elapsedTimer.Start();
        }
        else
        {
            _elapsedTimer.Stop();
        }

        if (recording && !reduce) StartPulse();
        else StopPulse();

        ShowLatest(_capture.LastScreenshotPath, reduce);
        ShowConflicts();
    }

    private void UpdateTimer() => TimerText.Text = ElapsedTime.Format(_capture.RecordingElapsed);

    private void StartPulse()
    {
        if (_pulsing) return;
        _pulsing = true;
        var pulse = new DoubleAnimation(1.0, 0.35, new Duration(PulseDuration))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        RecordDot.BeginAnimation(OpacityProperty, pulse);
    }

    private void StopPulse()
    {
        if (!_pulsing) return;
        _pulsing = false;
        RecordDot.BeginAnimation(OpacityProperty, null);
        RecordDot.Opacity = 1.0;
    }

    /// <summary>Loads the thumbnail only when the file changes; a new screenshot fades in, the first one is shown at once.</summary>
    private void ShowLatest(string? path, bool reduce)
    {
        if (path == _shownThumbnail && _shownOnce) return;

        _shownThumbnail = path;
        bool animate = _shownOnce;
        _shownOnce = true;

        BitmapImage? bitmap = LoadThumbnail(path);
        ThumbImage.Source = bitmap;
        ThumbEmpty.Visibility = bitmap is null ? Visibility.Visible : Visibility.Collapsed;
        ThumbFrame.Cursor = bitmap is null ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.Hand;
        if (bitmap is not null && animate) _thumbnailMotion.Play(reduce);
    }

    private static BitmapImage? LoadThumbnail(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // reads the file fully, so it is not locked afterwards
            bitmap.DecodePixelWidth = ThumbnailDecodeWidth;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void ShowConflicts()
    {
        var conflicts = new List<string>(2);
        if (_shortcuts is { PrintAvailable: false }) conflicts.Add("Ctrl+Alt+P");
        if (_shortcuts is { RecordAvailable: false }) conflicts.Add("Ctrl+Alt+R");

        ConflictPanel.Visibility = conflicts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ConflictText.Text = conflicts.Count > 0 ? $"{string.Join(" e ", conflicts)} em uso por outro app" : string.Empty;
    }

    private void CopyLatest()
    {
        string? path = _capture.LastScreenshotPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        try
        {
            _clipboard.SetImage(File.ReadAllBytes(path));
            CopyButton.LabelText = "Copiado";
            _copiedTimer.Stop();
            _copiedTimer.Start();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file was removed or is locked; nothing is copied and the label stays as it was.
        }
    }

    /// <summary>Shows the screenshot in Explorer with the file selected.</summary>
    private void OpenLatest()
    {
        string? path = _capture.LastScreenshotPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Explorer could not be started; nothing else to show.
        }
    }
}
