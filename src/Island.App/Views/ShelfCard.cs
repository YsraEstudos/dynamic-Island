using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.App.Animations;
using Island.App.Widgets;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Point = System.Windows.Point;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Island.App.Views;

/// <summary>Lifecycle of a card. Only <see cref="Normal"/> cards are placed by the shelf layout.</summary>
internal enum CardMode
{
    /// <summary>In the row, placed by the layout.</summary>
    Normal,
    /// <summary>Lifted and following the pointer during a reorder drag.</summary>
    Dragging,
    /// <summary>Following the pointer in the removal zone (reorder drag).</summary>
    Removing,
    /// <summary>Fading out after removal; not placed by the layout.</summary>
    Leaving,
    /// <summary>Drag placeholder for a widget being added from the tray.</summary>
    Ghost,
}

/// <summary>
/// One card on the shelf canvas: a widget or a drag ghost. Position, vertical offset, scale and opacity are
/// springs. The owner sets targets with <see cref="Pose"/>; the shelf's frame pump calls <see cref="Step"/> and
/// <see cref="Apply"/> while anything moves.
/// </summary>
internal sealed class ShelfCard
{
    /// <summary>Top of a card at rest: the 16 DIP padding of the widget band.</summary>
    public const double RestTop = IslandShapeTable.ShelfPadding;

    private const double SlotZeta = 0.8;
    private const double FollowZeta = 0.9;

    private static readonly double SlotOmega = SpringValue.OmegaForSettleTime(0.28, SlotZeta);
    private static readonly double FollowOmega = SpringValue.OmegaForSettleTime(0.16, FollowZeta);
    private static readonly double ScaleOmega = SpringValue.OmegaForSettleTime(0.24, 0.8);
    private static readonly double OpacityOmega = SpringValue.OmegaForSettleTime(0.2, 1.0);

    private static readonly Brush DashBrush = CreateDashBrush();

    private readonly ScaleTransform _transform = new(1.0, 1.0);
    private readonly System.Windows.Shapes.Rectangle _dashes;
    private readonly Border? _badge;

    public ShelfCard(WidgetDescriptor descriptor, FrameworkElement content, bool removable)
    {
        Descriptor = descriptor;
        Content = content;
        content.Width = descriptor.Width;
        content.Height = WidgetCatalog.WidgetHeight;

        X = new SpringValue(SlotOmega, SlotZeta, 0.0);
        Y = new SpringValue(SlotOmega, SlotZeta, 0.0);
        Scale = new SpringValue(ScaleOmega, 0.8, 1.0);
        Opacity = new SpringValue(OpacityOmega, 1.0, 1.0);

        _dashes = new System.Windows.Shapes.Rectangle
        {
            Stroke = DashBrush,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection(new[] { 4.0, 3.0 }),
            RadiusX = 16.0,
            RadiusY = 16.0,
            Stretch = Stretch.Fill,
            Margin = new Thickness(0.75),
            IsHitTestVisible = false,
            Visibility = System.Windows.Visibility.Collapsed,
        };

        // Transparent background makes the whole card a press target; the widget itself is non-interactive in edit mode.
        Root = new Grid
        {
            Width = descriptor.Width,
            Height = WidgetCatalog.WidgetHeight,
            Background = Brushes.Transparent,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _transform,
        };
        Root.Children.Add(content);
        Root.Children.Add(_dashes);

        if (removable)
        {
            _badge = BuildBadge();
            _badge.MouseLeftButtonDown += (_, e) => e.Handled = true;
            _badge.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                RemoveRequested?.Invoke();
            };
            Root.Children.Add(_badge);
        }
    }

    public WidgetDescriptor Descriptor { get; }
    public string Id => Descriptor.Id;
    public double Width => Descriptor.Width;
    public FrameworkElement Content { get; }
    public Grid Root { get; }

    /// <summary>Horizontal position of the card's left edge.</summary>
    public SpringValue X { get; }
    /// <summary>Vertical offset from <see cref="RestTop"/>.</summary>
    public SpringValue Y { get; }
    public SpringValue Scale { get; }
    public SpringValue Opacity { get; }

    public CardMode Mode { get; set; } = CardMode.Normal;

    /// <summary>True while any spring is still moving (set by <see cref="Step"/>).</summary>
    public bool Busy { get; private set; }

    /// <summary>Runs once when the card settles after its last <see cref="Pose"/>.</summary>
    public Action? Completion { get; set; }

    /// <summary>Set by the remove badge.</summary>
    public Action? RemoveRequested { get; set; }

    /// <summary>
    /// Sets the targets (or jumps to them when <paramref name="instant"/>). Use the current target to leave a
    /// value unchanged.
    /// </summary>
    public void Pose(double x, double y, double scale, double opacity, bool instant)
    {
        if (instant)
        {
            X.Snap(x);
            Y.Snap(y);
            Scale.Snap(scale);
            Opacity.Snap(opacity);
            Apply();
            return;
        }

        X.SetTarget(x);
        Y.SetTarget(y);
        Scale.SetTarget(scale);
        Opacity.SetTarget(opacity);
    }

    /// <summary>Softer slot springs for a card under the pointer, or the normal slot tuning.</summary>
    public void Tune(bool follow)
    {
        double omega = follow ? FollowOmega : SlotOmega;
        double zeta = follow ? FollowZeta : SlotZeta;
        X.Retune(omega, zeta);
        Y.Retune(omega, zeta);
    }

    /// <summary>Advances the springs by <paramref name="dt"/> seconds and records whether any is still moving.</summary>
    public void Step(double dt)
    {
        bool x = X.Advance(dt);
        bool y = Y.Advance(dt);
        bool scale = Scale.Advance(dt);
        bool opacity = Opacity.Advance(dt);
        Busy = !(x && y && scale && opacity);
    }

    /// <summary>Writes the spring values to the element.</summary>
    public void Apply()
    {
        Canvas.SetLeft(Root, X.Current);
        Canvas.SetTop(Root, RestTop + Y.Current);
        _transform.ScaleX = Scale.Current;
        _transform.ScaleY = Scale.Current;
        Root.Opacity = Math.Clamp(Opacity.Current, 0.0, 1.0);
    }

    /// <summary>Edit mode: content ignores the pointer, the dashed outline shows, and the badge appears (removable cards).</summary>
    public void SetEditing(bool editing)
    {
        Content.IsHitTestVisible = !editing;
        System.Windows.Visibility visibility = editing ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        _dashes.Visibility = visibility;
        if (_badge is not null) _badge.Visibility = visibility;
    }

    private static Border BuildBadge()
    {
        return new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = Resource<Brush>("MutedRedBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -7, -7, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Visibility = System.Windows.Visibility.Collapsed,
            Child = new System.Windows.Shapes.Rectangle
            {
                Width = 8,
                Height = 2,
                RadiusX = 1,
                RadiusY = 1,
                Fill = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private static Brush CreateDashBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(89, 255, 255, 255));   // white at about 35 % opacity
        brush.Freeze();
        return brush;
    }

    private static T Resource<T>(string key) where T : class =>
        (T)System.Windows.Application.Current.FindResource(key);
}
