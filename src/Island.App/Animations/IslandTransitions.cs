using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Island.App.Widgets;
using Island.Core.Configuration;
using Island.Core.Models;

namespace Island.App.Animations;

/// <summary>
/// Owns the motion of the single island shape and cross-switches its content layers.
///
/// - The shape (Width, Height, CornerRadius) is driven by three closed-form springs; height is slightly slower.
/// - A mode change swaps content in two strictly sequential phases: the outgoing layer fades and blurs out
///   (about 100 ms), and only then does the incoming layer enter (about 160 ms). Content springs are critically
///   damped, so content never overshoots. An optional swap hook runs in between, while nothing is visible, so an
///   owner can change a shared layer (for example the shelf's edit presentation) without a visible jump.
/// - Everything is stepped by one shared <see cref="MotionPump"/> client; the pump detaches when all motion settles.
/// - When <see cref="ReduceAnimations"/> is set, the shape snaps and content only cross-fades opacity.
/// </summary>
public sealed class IslandTransitions : IFrameClient
{
    private enum Phase
    {
        Idle,
        Out,
        In,
    }

    private const double WidthSettleSeconds = 0.30;
    private const double WidthZeta = 0.80;
    private const double HeightSettleSeconds = 0.36;
    private const double HeightZeta = 0.82;
    private const double RadiusSettleSeconds = 0.30;
    private const double RadiusZeta = 0.80;

    private const double OutDuration = 0.10;
    private const double InDuration = 0.16;
    private const double ReduceOutDuration = 0.06;
    private const double ReduceInDuration = 0.06;

    private const double OutBlur = 10.0;
    private const double OutScale = 0.96;
    private const double InBlur = 8.0;
    private const double InScale = 0.96;
    private const double InOffsetY = 4.0;

    private const double RootFadeDuration = 0.20;
    private const double ReduceRootFadeDuration = 0.12;

    private sealed class LayerState
    {
        public LayerState(UIElement layer)
        {
            Layer = layer;
            Scale = new ScaleTransform(1.0, 1.0);
            Translate = new TranslateTransform(0.0, 0.0);
            Blur = new BlurEffect { Radius = 0.0 };
            layer.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            layer.RenderTransform = new TransformGroup { Children = { Scale, Translate } };
        }

        public UIElement Layer { get; }
        public ScaleTransform Scale { get; }
        public TranslateTransform Translate { get; }
        public BlurEffect Blur { get; }
    }

    private readonly Border _shape;
    private readonly UIElement _root;
    private readonly Dictionary<UIElement, LayerState> _layers = new();

    private readonly SpringValue _width;
    private readonly SpringValue _height;
    private readonly SpringValue _radius;

    private readonly SpringValue _contentOpacity;
    private readonly SpringValue _contentBlur;
    private readonly SpringValue _contentScale;
    private readonly SpringValue _contentOffsetY;

    private readonly SpringValue _rootOpacity;

    private Phase _phase = Phase.Idle;
    private UIElement? _current;
    private UIElement? _outgoing;
    private UIElement? _incoming;
    private UIElement? _pending;
    private Action? _pendingBeforeIn;
    private double _phaseElapsed;

    private bool _rootFading;
    private double _rootElapsed;
    private double _rootDuration;
    private Action? _rootDone;

    public IslandTransitions(Border shape, UIElement root, IEnumerable<UIElement> layers, bool reduceAnimations)
    {
        _shape = shape;
        _root = root;
        ReduceAnimations = reduceAnimations;

        _width = new SpringValue(SpringValue.OmegaForSettleTime(WidthSettleSeconds, WidthZeta), WidthZeta, 124.0);
        _height = new SpringValue(SpringValue.OmegaForSettleTime(HeightSettleSeconds, HeightZeta), HeightZeta, 36.0);
        _radius = new SpringValue(SpringValue.OmegaForSettleTime(RadiusSettleSeconds, RadiusZeta), RadiusZeta, 18.0);

        _contentOpacity = new SpringValue(SpringValue.OmegaForSettleTime(InDuration, 1.0), 1.0, 1.0);
        _contentBlur = new SpringValue(SpringValue.OmegaForSettleTime(InDuration, 1.0), 1.0, 0.0);
        _contentScale = new SpringValue(SpringValue.OmegaForSettleTime(InDuration, 1.0), 1.0, 1.0);
        _contentOffsetY = new SpringValue(SpringValue.OmegaForSettleTime(InDuration, 1.0), 1.0, 0.0);

        _rootOpacity = new SpringValue(SpringValue.OmegaForSettleTime(RootFadeDuration, 1.0), 1.0, 1.0);

        foreach (UIElement layer in layers)
        {
            _layers[layer] = new LayerState(layer);
            layer.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>When true: no springs or blur, shape changes are instant and content only cross-fades opacity.</summary>
    public bool ReduceAnimations { get; set; }

    /// <summary>Moves the shape toward a size. Instant (or reduced motion) snaps without animation.</summary>
    public void SetShape(double width, double height, double radius, bool instant)
    {
        if (instant || ReduceAnimations)
        {
            _width.Snap(width);
            _height.Snap(height);
            _radius.Snap(radius);
            ApplyShape();
            return;
        }

        _width.SetTarget(width);
        _height.SetTarget(height);
        _radius.SetTarget(radius);
        MotionPump.Request(this);
    }

    /// <summary>
    /// Makes <paramref name="layer"/> the visible content. A non-instant request runs the out/in swap from the
    /// layer currently shown; repeated requests during a swap simply retarget the swap. <paramref name="beforeIn"/>
    /// runs once while the outgoing content is hidden and the incoming one has not yet appeared; it also lets the
    /// same layer be swapped with itself when its presentation changes.
    /// </summary>
    public void ShowLayer(UIElement layer, bool instant, Action? beforeIn = null)
    {
        if (!_layers.ContainsKey(layer))
        {
            throw new ArgumentException("The layer was not registered with the transitions.", nameof(layer));
        }

        if (instant)
        {
            HardSwitch(layer, beforeIn);
            return;
        }

        _pending = layer;
        _pendingBeforeIn = beforeIn;
        if (_phase != Phase.Idle) return;
        if (_current is null)
        {
            HardSwitch(layer, beforeIn);
            return;
        }
        if (ReferenceEquals(_current, layer) && beforeIn is null) return;

        BeginOut(_current);
    }

    /// <summary>Animates the whole island (window content) opacity; <paramref name="completed"/> runs when it settles.</summary>
    public void FadeRoot(double opacity, Action? completed)
    {
        double duration = ReduceAnimations ? ReduceRootFadeDuration : RootFadeDuration;
        _rootOpacity.Retune(SpringValue.OmegaForSettleTime(duration, 1.0), 1.0);
        _rootOpacity.SetTarget(opacity);
        _rootElapsed = 0.0;
        _rootDuration = duration;
        _rootDone = completed;
        _rootFading = true;
        MotionPump.Request(this);
    }

    /// <summary>Sets the root opacity immediately and cancels any running root fade.</summary>
    public void SnapRootOpacity(double opacity)
    {
        _rootFading = false;
        _rootDone = null;
        _rootOpacity.Snap(opacity);
        _root.Opacity = opacity;
    }

    bool IFrameClient.OnFrame(double dt) => OnFrame(dt);

    private bool OnFrame(double dt)
    {
        // Advance all three shape springs (no short-circuit: each must advance).
        bool widthAtRest = _width.Advance(dt);
        bool heightAtRest = _height.Advance(dt);
        bool radiusAtRest = _radius.Advance(dt);
        ApplyShape();
        bool busy = !(widthAtRest && heightAtRest && radiusAtRest);

        if (_phase != Phase.Idle)
        {
            busy = true;
            _phaseElapsed += dt;
            bool isOut = _phase == Phase.Out;
            double limit = isOut
                ? (ReduceAnimations ? ReduceOutDuration : OutDuration)
                : (ReduceAnimations ? ReduceInDuration : InDuration);

            if (_phaseElapsed >= limit)
            {
                if (isOut) FinishOut();
                else FinishIn();
            }
            else
            {
                _contentOpacity.Advance(dt);
                _contentBlur.Advance(dt);
                _contentScale.Advance(dt);
                _contentOffsetY.Advance(dt);
                ApplyContent(isOut ? _outgoing : _incoming);
            }
        }

        if (_rootFading)
        {
            _rootElapsed += dt;
            if (_rootElapsed >= _rootDuration)
            {
                _rootOpacity.Snap(_rootOpacity.Target);
                _root.Opacity = _rootOpacity.Current;
                _rootFading = false;
                Action? done = _rootDone;
                _rootDone = null;
                done?.Invoke();
            }
            else
            {
                _rootOpacity.Advance(dt);
                _root.Opacity = _rootOpacity.Current;
            }
        }

        return busy || _phase != Phase.Idle || _rootFading;
    }

    private void BeginOut(UIElement from)
    {
        _outgoing = from;
        _incoming = null;
        _phase = Phase.Out;
        _phaseElapsed = 0.0;

        bool reduce = ReduceAnimations;
        RetuneContent(reduce ? ReduceOutDuration : OutDuration);
        _contentOpacity.SetTarget(0.0);
        _contentBlur.SetTarget(reduce ? 0.0 : OutBlur);
        _contentScale.SetTarget(reduce ? 1.0 : OutScale);
        _contentOffsetY.SetTarget(0.0);
        MotionPump.Request(this);
    }

    private void FinishOut()
    {
        if (_outgoing is not null)
        {
            ResetLayer(_layers[_outgoing]);
            _outgoing.Visibility = Visibility.Collapsed;
        }
        _outgoing = null;

        // The owner's hook runs while nothing is visible, so a shared layer can change presentation here.
        Action? hook = _pendingBeforeIn;
        _pendingBeforeIn = null;
        hook?.Invoke();

        UIElement? next = _pending ?? _current;
        if (next is null)
        {
            _phase = Phase.Idle;
            return;
        }

        _incoming = next;
        _phase = Phase.In;
        _phaseElapsed = 0.0;
        next.Visibility = Visibility.Visible;

        bool reduce = ReduceAnimations;
        _contentOpacity.Snap(0.0);
        _contentBlur.Snap(reduce ? 0.0 : InBlur);
        _contentScale.Snap(reduce ? 1.0 : InScale);
        _contentOffsetY.Snap(reduce ? 0.0 : InOffsetY);
        RetuneContent(reduce ? ReduceInDuration : InDuration);
        _contentOpacity.SetTarget(1.0);
        _contentBlur.SetTarget(0.0);
        _contentScale.SetTarget(1.0);
        _contentOffsetY.SetTarget(0.0);
        ApplyContent(next);
    }

    private void FinishIn()
    {
        UIElement? finished = _incoming;
        _incoming = null;
        _phase = Phase.Idle;
        _current = finished;

        if (finished is not null) ResetLayer(_layers[finished]);
        ResetContentSprings();

        // A different layer requested meanwhile, or a same-layer swap hook, needs another out phase.
        if (_current is not null && _pending is not null
            && (!ReferenceEquals(_pending, _current) || _pendingBeforeIn is not null))
        {
            BeginOut(_current);
        }
    }

    private void HardSwitch(UIElement layer, Action? beforeIn)
    {
        _phase = Phase.Idle;
        _outgoing = null;
        _incoming = null;
        _pending = layer;
        _pendingBeforeIn = null;
        _current = layer;

        foreach (KeyValuePair<UIElement, LayerState> pair in _layers)
        {
            bool isTarget = ReferenceEquals(pair.Key, layer);
            pair.Key.Visibility = isTarget ? Visibility.Visible : Visibility.Collapsed;
            ResetLayer(pair.Value);
        }
        ResetContentSprings();

        beforeIn?.Invoke();
    }

    private void RetuneContent(double duration)
    {
        double omega = SpringValue.OmegaForSettleTime(duration, 1.0);
        _contentOpacity.Retune(omega, 1.0);
        _contentBlur.Retune(omega, 1.0);
        _contentScale.Retune(omega, 1.0);
        _contentOffsetY.Retune(omega, 1.0);
    }

    private void ResetContentSprings()
    {
        _contentOpacity.Snap(1.0);
        _contentBlur.Snap(0.0);
        _contentScale.Snap(1.0);
        _contentOffsetY.Snap(0.0);
    }

    private void ApplyShape()
    {
        _shape.Width = Math.Max(1.0, _width.Current);
        _shape.Height = Math.Max(1.0, _height.Current);
        _shape.CornerRadius = new CornerRadius(Math.Max(0.0, _radius.Current));
    }

    private void ApplyContent(UIElement? element)
    {
        if (element is null || !_layers.TryGetValue(element, out LayerState? state)) return;

        double blur = Math.Max(0.0, _contentBlur.Current);
        element.Opacity = Math.Clamp(_contentOpacity.Current, 0.0, 1.0);
        state.Blur.Radius = blur;
        // Effect is only attached during the transition so text is rendered crisp at rest.
        element.Effect = blur > 0.05 ? state.Blur : null;

        double scale = _contentScale.Current;
        state.Scale.ScaleX = scale;
        state.Scale.ScaleY = scale;
        state.Translate.Y = _contentOffsetY.Current;
    }

    private static void ResetLayer(LayerState state)
    {
        state.Layer.Opacity = 1.0;
        state.Layer.Effect = null;
        state.Scale.ScaleX = 1.0;
        state.Scale.ScaleY = 1.0;
        state.Translate.Y = 0.0;
        state.Blur.Radius = 0.0;
    }
}

/// <summary>Width, height and corner radius of the island shape, in DIPs.</summary>
public readonly record struct ShapeSize(double Width, double Height, double Radius);

/// <summary>
/// Size table of every island mode (the host window is 1120 x 330 and the island grows downward from its top edge).
/// Expanded and Customize widths follow the widget ids they show; unknown ids are ignored.
/// </summary>
public static class IslandShapeTable
{
    /// <summary>Padding between the shape edge and the widget row.</summary>
    public const double ShelfPadding = 16.0;
    /// <summary>Gap between neighbouring widgets.</summary>
    public const double ShelfGap = 12.0;
    public const double ShelfMinWidth = 400.0;
    /// <summary>Widest shelf. The host window is wider than this so the shape never clips.</summary>
    public const double ShelfMaxWidth = 1080.0;
    /// <summary>Widget band: padding, one widget row, padding.</summary>
    public const double ShelfHeight = WidgetCatalog.WidgetHeight + 2 * ShelfPadding;
    /// <summary>Customize tray below the widget band.</summary>
    public const double TrayHeight = 104.0;
    public const double CustomizeMinWidth = 640.0;
    public const double ShelfRadius = 38.0;
    public const double PomodoroCompactWidth = 150.0;
    public const double PendingTaskCompactLength = 140.0;
    /// <summary>Compact capsule length while the idle content (weather, or a scheduled start) is shown. Horizontal only.</summary>
    public const double IdleCompactWidth = 170.0;

    /// <summary>Known widgets among <paramref name="ids"/>, in order, each id at most once.</summary>
    public static IReadOnlyList<WidgetDescriptor> Resolve(IEnumerable<string> ids)
    {
        var result = new List<WidgetDescriptor>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            if (!seen.Add(id)) continue;
            WidgetDescriptor? descriptor = WidgetCatalog.Find(id);
            if (descriptor is not null) result.Add(descriptor);
        }
        return result;
    }

    /// <summary>Unclamped width of a row: sum of widths, gaps and padding.</summary>
    public static double RawWidth(IReadOnlyList<WidgetDescriptor> widgets)
    {
        double sum = 0.0;
        foreach (WidgetDescriptor widget in widgets) sum += widget.Width;
        return sum + ShelfGap * Math.Max(0, widgets.Count - 1) + 2 * ShelfPadding;
    }

    /// <summary>
    /// The longest prefix of <paramref name="ids"/> (after <see cref="Resolve"/>) whose row fits in
    /// <see cref="ShelfMaxWidth"/>; widgets are dropped from the end. In memory only: settings are not rewritten.
    /// </summary>
    public static IReadOnlyList<string> FitIds(IEnumerable<string> ids)
    {
        List<WidgetDescriptor> widgets = Resolve(ids).ToList();
        while (widgets.Count > 0 && RawWidth(widgets) > ShelfMaxWidth)
        {
            widgets.RemoveAt(widgets.Count - 1);
        }
        return widgets.Select(w => w.Id).ToArray();
    }

    /// <summary>Each row fitted to the maximum width (see <see cref="FitIds"/>). Always at least one row, possibly empty.</summary>
    public static List<List<string>> FitRows(IEnumerable<IEnumerable<string>> rows)
    {
        var result = new List<List<string>>();
        foreach (IEnumerable<string> row in rows)
        {
            List<string> fitted = FitIds(row).ToList();
            if (fitted.Count > 0) result.Add(fitted);
        }
        if (result.Count == 0) result.Add(new List<string>());
        return result;
    }

    /// <summary>True when adding <paramref name="id"/> to <paramref name="ids"/> keeps the row within the maximum width.</summary>
    public static bool Fits(IEnumerable<string> ids, string id) =>
        RawWidth(Resolve(ids.Append(id)).ToList()) <= ShelfMaxWidth;

    /// <summary>Shelf width for the given widgets, after fitting them, clamped to 400..1080.</summary>
    public static double ShelfWidth(IEnumerable<string> ids)
    {
        IReadOnlyList<WidgetDescriptor> widgets = Resolve(FitIds(ids));
        return Math.Clamp(RawWidth(widgets), ShelfMinWidth, ShelfMaxWidth);
    }

    /// <summary>
    /// Target shape for a mode. <paramref name="shelfIds"/> is the saved list for Expanded and the working list for Customize.
    /// <paramref name="vertical"/> is the docked form: only Compact, Volume and Mini change, the other modes stay horizontal.
    /// <paramref name="idleShown"/> widens the Compact capsule for the idle content (see <see cref="IdleCompactWidth"/>).
    /// </summary>
    public static ShapeSize For(IslandMode mode, IslandSettings settings, bool pomodoroRunning,
        bool hasPendingTasks, IEnumerable<string> shelfIds, bool vertical = false, bool idleShown = false) =>
        mode switch
        {
            IslandMode.Mini => vertical ? new ShapeSize(16.0, 56.0, 8.0) : new ShapeSize(56.0, 16.0, 8.0),
            IslandMode.Volume => vertical ? new ShapeSize(48.0, 200.0, 24.0) : new ShapeSize(300.0, 48.0, 24.0),
            IslandMode.MediaPreview => new ShapeSize(360.0, 68.0, 30.0),
            IslandMode.Notice => new ShapeSize(320.0, 56.0, 28.0),
            IslandMode.Expanded => new ShapeSize(ShelfWidth(shelfIds), ShelfHeight, ShelfRadius),
            IslandMode.Customize => new ShapeSize(
                Math.Max(CustomizeMinWidth, ShelfWidth(shelfIds)), ShelfHeight + TrayHeight, ShelfRadius),
            IslandMode.Clipboard => new ShapeSize(760.0, 232.0, 34.0),
            _ => CompactShape(settings, pomodoroRunning, hasPendingTasks, vertical, idleShown),
        };

    /// <summary>Compatibility overload for callers that do not provide calendar state.</summary>
    public static ShapeSize For(IslandMode mode, IslandSettings settings, bool pomodoroRunning,
        IEnumerable<string> shelfIds, bool vertical = false) =>
        For(mode, settings, pomodoroRunning, false, shelfIds, vertical);

    private static ShapeSize CompactShape(IslandSettings settings, bool pomodoroRunning, bool hasPendingTasks, bool vertical, bool idleShown)
    {
        double height = settings.CompactHeight;
        double width = Math.Max(settings.CompactWidth, hasPendingTasks ? PendingTaskCompactLength : 0.0);
        if (pomodoroRunning) width = Math.Max(width, PomodoroCompactWidth);
        // The vertical capsule keeps its length: its idle content is one value, which fits the length it already has.
        if (idleShown && !vertical) width = Math.Max(width, IdleCompactWidth);
        // Docked, the capsule's thickness runs across the screen and its length runs along the edge.
        return vertical
            ? new ShapeSize(height, width, height / 2.0)
            : new ShapeSize(width, height, height / 2.0);
    }
}
