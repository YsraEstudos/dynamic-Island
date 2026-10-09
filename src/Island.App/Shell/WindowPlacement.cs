using System.Windows;
using Point = System.Windows.Point;

namespace Island.App.Shell;

/// <summary>Screen-space helpers (DIPs) for windows that grow out of a widget.</summary>
public static class WindowPlacement
{
    /// <summary>The element's bounds on screen in DIPs, or null when it is not currently shown.</summary>
    public static Rect? ScreenBounds(FrameworkElement element)
    {
        if (!element.IsLoaded || !element.IsVisible || element.ActualWidth < 1 || element.ActualHeight < 1) return null;
        if (PresentationSource.FromVisual(element) is not { CompositionTarget: { } target }) return null;

        var toDips = target.TransformFromDevice;
        Point topLeft = toDips.Transform(element.PointToScreen(new Point(0, 0)));
        Point bottomRight = toDips.Transform(element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight)));
        return new Rect(topLeft, bottomRight);
    }

    /// <summary>Keeps <paramref name="rect"/> inside <paramref name="area"/>, shrinking it only when it cannot fit.</summary>
    public static Rect Clamp(Rect rect, Rect area)
    {
        double width = Math.Min(rect.Width, area.Width);
        double height = Math.Min(rect.Height, area.Height);
        double x = Math.Min(Math.Max(rect.X, area.Left), area.Right - width);
        double y = Math.Min(Math.Max(rect.Y, area.Top), area.Bottom - height);
        return new Rect(x, y, width, height);
    }

    public static Rect WorkArea => SystemParameters.WorkArea;
}
