using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Brush = System.Windows.Media.Brush;

namespace Island.App.Widgets;

/// <summary>Static 24-unit vector icon (outline by default, filled on request), for decorative use.</summary>
public static class WidgetGlyph
{
    public static Viewbox Create(Geometry geometry, Brush brush, double size, bool filled = false)
    {
        var path = new Path
        {
            Data = geometry,
            Fill = filled ? brush : null,
            Stroke = filled ? null : brush,
            StrokeThickness = filled ? 0.0 : 1.8,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };

        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);

        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = canvas,
        };
    }

    /// <summary>Looks up a Geometry resource (returns null when the key is missing).</summary>
    public static Geometry? Find(string key) =>
        System.Windows.Application.Current?.TryFindResource(key) as Geometry;
}
