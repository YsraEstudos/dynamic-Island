using System.Windows;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace Island.App.Shell;

/// <summary>Full-canvas visual with a static annular hit region; transparent gaps never select a neighbour.</summary>
public sealed class RadialSectorButton : Button
{
    public Geometry Sector { get; init; } = Geometry.Empty;
    public double Angle { get; init; }
    public System.Windows.Media.Color Accent { get; init; }
    private static readonly Pen HitOutline = new(System.Windows.Media.Brushes.Transparent, 10) { LineJoin = PenLineJoin.Round };

    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters) =>
        Sector.FillContains(parameters.HitPoint) || Sector.StrokeContains(HitOutline, parameters.HitPoint)
            ? new PointHitTestResult(this, parameters.HitPoint) : null;

    public static Geometry CreateSector(int index, int count)
    {
        double step = 360d / count, start = index * step - step / 2, end = start + step;
        const double outer = 245, inner = 105;
        double OuterGap = 9 / outer * 180 / Math.PI, InnerGap = 9 / inner * 180 / Math.PI;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(Polar(start + OuterGap, outer), true, true);
            context.ArcTo(Polar(end - OuterGap, outer), new Size(outer, outer), 0, step > 180, SweepDirection.Clockwise, true, false);
            context.LineTo(Polar(end - InnerGap, inner), true, false);
            context.ArcTo(Polar(start + InnerGap, inner), new Size(inner, inner), 0, step > 180, SweepDirection.Counterclockwise, true, false);
        }
        geometry.Freeze(); return geometry;
    }

    public static Point Polar(double degrees, double radius) => new(
        260 + Math.Sin(degrees * Math.PI / 180) * radius,
        260 - Math.Cos(degrees * Math.PI / 180) * radius);
}
