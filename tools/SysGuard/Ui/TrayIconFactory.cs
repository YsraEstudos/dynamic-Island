using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SysGuard;

/// <summary>Ícone mínimo da bandeja: escudo escuro com contorno claro. Sem nenhuma menção a conteúdo.</summary>
internal static class TrayIconFactory
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var shield = ShieldPath();
            using var fill = new SolidBrush(Color.FromArgb(255, 42, 44, 48));
            using var outline = new Pen(Color.FromArgb(255, 236, 238, 240), 2f);
            graphics.FillPath(fill, shield);
            graphics.DrawPath(outline, shield);

            using var check = new Pen(Color.FromArgb(255, 120, 205, 150), 2.4f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            graphics.DrawLines(check,
            [
                new PointF(10f, 16.5f),
                new PointF(14.5f, 21f),
                new PointF(22f, 11.5f),
            ]);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath ShieldPath()
    {
        var path = new GraphicsPath();
        path.AddPolygon(
        [
            new PointF(16f, 2.5f),
            new PointF(27f, 7f),
            new PointF(27f, 15f),
            new PointF(16f, 29.5f),
            new PointF(5f, 15f),
            new PointF(5f, 7f),
        ]);
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
