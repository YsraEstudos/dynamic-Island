using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Island.App.Mixer;
using Island.App.Widgets;
using Island.Core.Audio;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
using Path = System.Windows.Shapes.Path;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Island.App.Views.Widgets;

/// <summary>
/// One app in the mixer list: icon, name and percent, volume slider, a thin peak bar under the slider, and a mute button.
/// Built once per session and updated in place, so a meter tick only moves the bar.
/// </summary>
internal sealed class MixerRow
{
    /// <summary>Row height in DIPs; the list is three rows high.</summary>
    public const double Height = 28;

    private static readonly Brush TextPrimary = Frozen(0xFF, 0xFF, 0xFF);
    private static readonly Brush TextTertiary = Frozen(0x6E, 0x6E, 0x73);
    private static readonly Brush PeakTrack = Frozen(0x2C, 0x2C, 0x2E);
    private static readonly Brush PeakFill = Frozen(0x30, 0xD1, 0x58);
    private static readonly Brush ButtonBackground = Frozen(0x2C, 0x2C, 0x2E);
    private static readonly Brush ButtonIcon = Frozen(0xE5, 0xE5, 0xEA);

    private readonly string _id;
    private readonly Geometry _speaker;
    private readonly Geometry _speakerMuted;
    private readonly Action<string, int> _setVolume;
    private readonly Action<string, bool> _setMuted;
    private readonly Grid _root = new() { Width = 252, Height = Height };
    private readonly Image _icon;
    private readonly Path _fallbackIcon;
    private readonly TextBlock _name;
    private readonly TextBlock _percent;
    private readonly VolumeSlider _slider;
    private readonly ScaleTransform _peakScale = new(0.0, 1.0);
    private readonly ShelfButton _mute;
    private string? _iconPath;
    private bool _mutedIconShown;
    private AppAudioSession? _session;

    public MixerRow(string id, Geometry speaker, Geometry speakerMuted, Action<string, int> setVolume, Action<string, bool> setMuted)
    {
        _id = id;
        _speaker = speaker;
        _speakerMuted = speakerMuted;
        _setVolume = setVolume;
        _setMuted = setMuted;

        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });

        // Icon: the executable's icon when there is one, otherwise a generic speaker glyph.
        var iconHost = new Grid { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
        _icon = new Image { Width = 16, Height = 16, Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
        RenderOptions.SetBitmapScalingMode(_icon, BitmapScalingMode.HighQuality);
        _fallbackIcon = new Path
        {
            Data = speaker,
            Stroke = ButtonIcon,
            StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
        };
        iconHost.Children.Add(_icon);
        iconHost.Children.Add(_fallbackIcon);
        Grid.SetColumn(iconHost, 0);
        _root.Children.Add(iconHost);

        _name = new TextBlock
        {
            FontSize = 11,
            Foreground = TextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _name.SetResourceReference(TextBlock.FontFamilyProperty, "IslandFontFamily");

        _percent = new TextBlock
        {
            FontSize = 10,
            Foreground = TextTertiary,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var header = new Grid { Height = 13 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_name, 0);
        Grid.SetColumn(_percent, 1);
        header.Children.Add(_name);
        header.Children.Add(_percent);

        _slider = new VolumeSlider { Height = 9, Margin = new Thickness(0, 1, 0, 0) };
        _slider.UserChanged += level => _setVolume(_id, level);

        // Peak bar: a full-width track with a green fill scaled from the left, so a meter tick is one transform change.
        var peakTrack = new Border { Background = PeakTrack, CornerRadius = new CornerRadius(1), Height = 2 };        var peakFill = new Border
        {
            Background = PeakFill,
            CornerRadius = new CornerRadius(1),
            Height = 2,
            RenderTransform = _peakScale,
            RenderTransformOrigin = new Point(0, 0.5),
        };
        var peak = new Grid { Height = 2, Margin = new Thickness(0, 2, 0, 0) };
        peak.Children.Add(peakTrack);
        peak.Children.Add(peakFill);

        var middle = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        middle.Children.Add(header);
        middle.Children.Add(_slider);
        middle.Children.Add(peak);
        Grid.SetColumn(middle, 2);
        _root.Children.Add(middle);

        _mute = new ShelfButton
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = ButtonBackground,
            Icon = speaker,
            IconBrush = ButtonIcon,
            IconSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(_mute, "Silenciar aplicativo");
        _mute.Click += () =>
        {
            if (_session is { } session) _setMuted(_id, !session.Muted);
        };
        Grid.SetColumn(_mute, 4);
        _root.Children.Add(_mute);
    }

    public FrameworkElement Root => _root;

    public string Id => _id;

    /// <summary>Applies the latest state of the session. Cheap when nothing changed.</summary>
    public void Update(AppAudioSession session)
    {
        _session = session;

        if (!string.Equals(session.ExecutablePath, _iconPath, StringComparison.OrdinalIgnoreCase))
        {
            _iconPath = session.ExecutablePath;
            ImageSource? icon = AppIconCache.Get(_iconPath);
            _icon.Source = icon;
            _icon.Visibility = icon is null ? Visibility.Collapsed : Visibility.Visible;
            _fallbackIcon.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_name.Text != session.Name) _name.Text = session.Name;
        string percent = session.Muted ? "mudo" : $"{session.Volume}%";
        if (_percent.Text != percent) _percent.Text = percent;

        if (!_slider.IsDragging && _slider.Value != session.Volume) _slider.Value = session.Volume;

        if (_mutedIconShown != session.Muted)
        {
            _mutedIconShown = session.Muted;
            _mute.Icon = session.Muted ? _speakerMuted : _speaker;
        }

        _peakScale.ScaleX = AudioVolumeMath.ClampPeak(session.Peak);
        _root.Opacity = session.Active ? 1.0 : 0.4;
    }

    private static Brush Frozen(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
