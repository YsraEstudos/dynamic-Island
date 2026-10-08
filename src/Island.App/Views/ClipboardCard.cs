using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.App.Widgets;
using Island.Core.Clipboard;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Island.App.Views;

/// <summary>
/// One clipboard card (196 x 172). Content depends on the kind: text, link (preview band with host and URL), image
/// (fills the card) or colour (filled with the colour and its hex code). Left click copies the item back; right click
/// reveals a Delete pill that stays until the pointer leaves the card.
/// </summary>
public sealed class ClipboardCard : Border
{
    public const double CardWidth = 196.0;
    public const double CardHeight = 160.0;
    private const double Radius = 18.0;
    private const int MaxTextLength = 600;

    private static readonly Brush CardFill = Frozen(0x16, 0x16, 0x16);
    private static readonly Brush StrokeFill = Frozen(0x2A, 0x2A, 0x2A);
    private static readonly Brush LatestStroke = Frozen(0xF2, 0xF2, 0xF7);
    private static readonly Brush PillFill = Frozen(0x2A, 0x2A, 0x2A);
    private static readonly Brush PlaceholderFill = Frozen(0x1C, 0x1C, 0x1E);
    private static readonly Brush LinkBandFill = Frozen(0x2A, 0x2A, 0x2A);
    private static readonly Brush SecondaryText = Frozen(0xA1, 0xA1, 0xA6);
    private static readonly Brush PrimaryText = Brushes.White;
    private static readonly Brush DeleteFill = Frozen(0xFF, 0x45, 0x3A);

    public static readonly DependencyProperty EntryProperty = DependencyProperty.Register(
        nameof(Entry), typeof(ClipboardEntry), typeof(ClipboardCard), new PropertyMetadata(null, OnEntryChanged));

    private readonly Grid _content = new();
    private readonly Border _pill;
    private readonly ContentControl _pillIcon;
    private readonly TextBlock _pillLabel;
    private readonly ShelfButton _deleteButton;
    private readonly PressFeedback _press;
    private ClipboardEntry? _observed;
    private Brush _cardBackground = CardFill;

    public ClipboardCard()
    {
        Width = CardWidth;
        Height = CardHeight;
        CornerRadius = new CornerRadius(Radius);
        BorderThickness = new Thickness(1);
        BorderBrush = StrokeFill;
        Background = CardFill;

        var root = new Grid();
        root.Children.Add(_content);

        _pillIcon = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        _pillLabel = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = PrimaryText,
            Margin = new Thickness(5, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _pillLabel.SetResourceReference(TextBlock.FontFamilyProperty, "IslandFontFamily");

        var pillPanel = new StackPanel { Orientation = Orientation.Horizontal };
        pillPanel.Children.Add(_pillIcon);
        pillPanel.Children.Add(_pillLabel);

        _pill = new Border
        {
            CornerRadius = new CornerRadius(11),
            Background = PillFill,
            Padding = new Thickness(8, 3, 10, 3),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 10),
            Child = pillPanel,
        };
        root.Children.Add(_pill);

        _deleteButton = new ShelfButton
        {
            LabelText = "Delete",
            LabelBrush = PrimaryText,
            Background = DeleteFill,
            CornerRadius = new CornerRadius(12),
            Width = 76,
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 9),
            Visibility = Visibility.Collapsed,
        };
        _deleteButton.Click += () => Entry?.Delete();
        root.Children.Add(_deleteButton);

        Child = root;

        _press = new PressFeedback(this, 0.97);
        _press.Clicked += () => Entry?.Copy();

        MouseRightButtonDown += (_, e) =>
        {
            ShowDelete(true);
            e.Handled = true;
        };
        MouseLeave += (_, _) => ShowDelete(false);
        SizeChanged += (_, _) => UpdateClip();
        Loaded += (_, _) =>
        {
            Observe(Entry);
            UpdateStroke();
        };
        Unloaded += (_, _) => Observe(null);
    }

    public ClipboardEntry? Entry
    {
        get => (ClipboardEntry?)GetValue(EntryProperty);
        set => SetValue(EntryProperty, value);
    }

    private static void OnEntryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var card = (ClipboardCard)d;
        card.Observe(e.NewValue as ClipboardEntry);
        card.Rebuild();
    }

    /// <summary>Tracks the entry's IsLatest flag while the card is in the tree (no handler leaks when it is recycled).</summary>
    private void Observe(ClipboardEntry? entry)
    {
        if (ReferenceEquals(entry, _observed)) return;

        if (_observed is not null) _observed.PropertyChanged -= OnEntryPropertyChanged;
        _observed = IsLoaded ? entry : null;
        if (_observed is not null) _observed.PropertyChanged += OnEntryPropertyChanged;
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClipboardEntry.IsLatest)) UpdateStroke();
    }

    private void Rebuild()
    {
        _content.Children.Clear();
        ShowDelete(false);

        ClipboardEntry? entry = Entry;
        if (entry is null)
        {
            _cardBackground = CardFill;
            Background = CardFill;
            return;
        }

        _cardBackground = CardFill;
        switch (entry.Kind)
        {
            case ClipboardKind.Text:
                BuildText(entry.Text);
                break;
            case ClipboardKind.Link:
                BuildLink(entry.Text);
                break;
            case ClipboardKind.Image:
                BuildImage(entry);
                break;
            case ClipboardKind.Color:
                BuildColor(entry.Text);
                break;
        }

        Background = _cardBackground;
        SetPill(entry.Kind);
        UpdateStroke();
        UpdateClip();
    }

    private void BuildText(string text)
    {
        var block = new TextBlock
        {
            Text = Truncate(text),
            Foreground = PrimaryText,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(14, 14, 14, 40),
            MaxHeight = 6 * 17.0,
            VerticalAlignment = VerticalAlignment.Top,
        };
        block.SetResourceReference(TextBlock.FontFamilyProperty, "IslandFontFamily");
        _content.Children.Add(block);
    }

    private void BuildLink(string url)
    {
        Viewbox glyph = WidgetGlyph.Create(WidgetGlyph.Find("WidgetIcon.Link") ?? Geometry.Empty, SecondaryText, 26);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;

        var band = new Border
        {
            Height = 64,
            VerticalAlignment = VerticalAlignment.Top,
            Background = LinkBandFill,
            Child = glyph,
        };
        _content.Children.Add(band);

        var host = new TextBlock
        {
            Text = HostOf(url),
            FontSize = 11,
            Foreground = SecondaryText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        host.SetResourceReference(TextBlock.FontFamilyProperty, "IslandFontFamily");

        var full = new TextBlock
        {
            Text = Truncate(url),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = PrimaryText,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 2 * 17.0,
            Margin = new Thickness(0, 2, 0, 0),
        };
        full.SetResourceReference(TextBlock.FontFamilyProperty, "IslandFontFamily");

        var stack = new StackPanel
        {
            Margin = new Thickness(14, 72, 14, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        stack.Children.Add(host);
        stack.Children.Add(full);
        _content.Children.Add(stack);
    }

    private void BuildImage(ClipboardEntry entry)
    {
        System.Windows.Media.Imaging.BitmapImage? image = entry.GetImage();
        Brush fill = image is null
            ? PlaceholderFill
            : new ImageBrush(image) { Stretch = Stretch.UniformToFill };

        _content.Children.Add(new Border { Background = fill });
    }

    private void BuildColor(string text)
    {
        Color color = TryParseHex(text, out Color parsed) ? parsed : Color.FromRgb(0x3A, 0x3A, 0x3C);
        Brush fill = Frozen(color.R, color.G, color.B, color.A);
        _cardBackground = fill;

        bool light = Luminance(color) > 0.6;
        var label = new TextBlock
        {
            Text = text.Trim(),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = light ? Brushes.Black : Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetResourceReference(TextBlock.FontFamilyProperty, "IslandFontFamily");
        _content.Children.Add(label);
    }

    private void SetPill(ClipboardKind kind)
    {
        (string label, string iconKey, bool filled) = kind switch
        {
            ClipboardKind.Text => ("Text", "WidgetIcon.Text", false),
            ClipboardKind.Link => ("Link", "WidgetIcon.Link", false),
            ClipboardKind.Image => ("Image", "WidgetIcon.Image", false),
            _ => ("Color", "WidgetIcon.Color", true),
        };

        _pillLabel.Text = label;
        _pillIcon.Content = WidgetGlyph.Create(WidgetGlyph.Find(iconKey) ?? Geometry.Empty, PrimaryText, 12, filled);
    }

    private void UpdateStroke()
    {
        bool latest = Entry?.IsLatest == true;
        BorderThickness = latest ? new Thickness(2) : new Thickness(1);
        BorderBrush = latest ? LatestStroke : StrokeFill;
    }

    private void UpdateClip()
    {
        // The rounded clip keeps the image, link band and colour fill inside the card corners.
        Clip = new RectangleGeometry(new Rect(0, 0, CardWidth, CardHeight), Radius - 1, Radius - 1);
    }

    private void ShowDelete(bool show)
    {
        _deleteButton.Visibility = show && Entry is not null ? Visibility.Visible : Visibility.Collapsed;
        _pill.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string Truncate(string text) =>
        text.Length > MaxTextLength ? text[..MaxTextLength] : text;

    private static string HostOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || string.IsNullOrEmpty(uri.Host)) return url;
        return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
    }

    /// <summary>Parses #RGB, #RRGGBB and #RRGGBBAA (CSS order, alpha last).</summary>
    private static bool TryParseHex(string text, out Color color)
    {
        color = default;
        string hex = text.Trim();
        if (hex.StartsWith('#')) hex = hex[1..];
        if (hex.Length is not (3 or 4 or 6 or 8)) return false;
        if (!hex.All(char.IsAsciiHexDigit)) return false;

        string expanded = hex.Length <= 4 ? string.Concat(hex.Select(ch => new string(ch, 2))) : hex;
        byte r = Convert.ToByte(expanded.Substring(0, 2), 16);
        byte g = Convert.ToByte(expanded.Substring(2, 2), 16);
        byte b = Convert.ToByte(expanded.Substring(4, 2), 16);
        byte a = expanded.Length == 8 ? Convert.ToByte(expanded.Substring(6, 2), 16) : (byte)255;
        color = Color.FromArgb(a, r, g, b);
        return true;
    }

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    private static Brush Frozen(byte r, byte g, byte b, byte a = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
