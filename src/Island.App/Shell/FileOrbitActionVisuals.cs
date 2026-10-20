using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Island.Core.Files;
using Button = System.Windows.Controls.Button;
using Binding = System.Windows.Data.Binding;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Path = System.Windows.Shapes.Path;

namespace Island.App.Shell;

internal static class FileOrbitActionVisuals
{
    public static FrameworkElement Create(RadialSectorButton button, FileAction action)
    {
        var content = new Canvas { Width = 520, Height = 520, IsHitTestVisible = false };
        NameScope.SetNameScope(content, new NameScope());
        var shape = new Path
        {
            Name = "SectorShape", Data = button.Sector, Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 27, 32)),
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 27, 32)), StrokeThickness = 10,
            StrokeLineJoin = PenLineJoin.Round
        };
        content.RegisterName("SectorShape", shape); content.Children.Add(shape);
        var icon = new Path
        {
            Name = "ActionGlyph", Data = Geometry.Parse(GeometryFor(action)), Width = 31.2, Height = 31.2,
            Stretch = Stretch.Uniform, StrokeThickness = 1.65, StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            RenderTransformOrigin = new System.Windows.Point(.5, .5)
        };
        icon.SetBinding(Path.StrokeProperty, new Binding("Foreground") { Source = button });
        content.RegisterName("ActionGlyph", icon);
        var position = RadialSectorButton.Polar(button.Angle, 164);
        Canvas.SetLeft(icon, position.X - 15.6); Canvas.SetTop(icon, position.Y - 15.6); content.Children.Add(icon);
        var label = new TextBlock
        {
            Name = "ActionLabel", Text = Label(action), FontSize = 16, FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center, Width = 130, Height = 25
        };
        label.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { Source = button });
        var caption = RadialSectorButton.Polar(button.Angle, 206);
        // The demo uses six short labels. Real batches can have four, five or seven actions;
        // near horizontal directions need the caption below its icon to avoid crossing the glyph.
        if (Math.Abs(Math.Sin(button.Angle * Math.PI / 180)) > .9)
            caption = new System.Windows.Point(position.X, position.Y + 36);
        Canvas.SetLeft(label, caption.X - 65); Canvas.SetTop(label, caption.Y - 12.5);
        content.RegisterName("ActionLabel", label); content.Children.Add(label);
        return content;
    }

    internal static string Label(FileAction action) => action switch
    {
        FileAction.Rename => "Renomear", FileAction.Duplicates => "Duplicados", FileAction.Package => "ZIP",
        FileAction.Index => "Índice", FileAction.ContactSheet => "Mosaico", FileAction.Images => "Imagens",
        FileAction.ImagesPdf => "PDF", FileAction.OfficePdf => "PDF", FileAction.MergePdf => "Juntar PDF",
        FileAction.SplitPdf => "Dividir PDF", FileAction.CompressPdf => "Comprimir", FileAction.Video => "Vídeo",
        FileAction.Audio => "Áudio", FileAction.Extract => "Extrair", FileAction.CsvToXlsx => "XLSX", _ => "CSV"
    };

    private static string GeometryFor(FileAction action) => action switch
    {
        FileAction.Rename => "M4,7 H13 M4,12 H10 M4,17 H8 M12,17 L18,11 L21,14 L15,20 L11,21 Z M17,12 L20,15",
        FileAction.Duplicates => "M8,8 H20 V21 H8 Z M4,16 H3 V3 H16 V4",
        FileAction.Package => "M4,7 L12,3 L20,7 V17 L12,21 L4,17 Z M4,7 L12,11 L20,7 M12,11 V21 M8,5 L16,9 V13",
        FileAction.Index => "M4,3 H17 L21,7 V21 H4 Z M17,3 V7 H21 M8,11 H17 M8,15 H17 M8,18 H14",
        FileAction.ContactSheet => "M3,3 H21 V21 H3 Z M3,9 H21 M3,15 H21 M9,3 V21 M15,3 V21",
        FileAction.Images => "M3,4 H21 V20 H3 Z M3,16 L9,10 L14,15 L17,12 L21,16 M15,8 A1.5,1.5 0 1 0 18,8 A1.5,1.5 0 1 0 15,8",
        FileAction.ImagesPdf or FileAction.OfficePdf => "M5,2 H15 L20,7 V22 H5 Z M15,2 V7 H20 M8,11 H17 M8,15 H17 M8,19 H13",
        FileAction.MergePdf => "M3,4 H9 V14 H3 Z M15,4 H21 V14 H15 Z M6,17 L12,21 L18,17 M12,14 V21",
        FileAction.SplitPdf => "M9,2 H15 V10 H9 Z M12,10 V14 M5,14 H19 M5,14 V18 M19,14 V18 M2,18 H8 V23 H2 Z M16,18 H22 V23 H16 Z",
        FileAction.CompressPdf => "M5,3 H19 V21 H5 Z M8,9 L12,12 L16,9 M12,6 V12 M8,17 L12,14 L16,17 M12,14 V20",
        FileAction.Video => "M3,4 H21 V20 H3 Z M10,8 L16,12 L10,16 Z",
        FileAction.Audio => "M9,18 V5 L20,3 V16 M9,8 L20,6 M9,18 A3,2 0 1 0 3,18 A3,2 0 1 0 9,18 M20,16 A3,2 0 1 0 14,16 A3,2 0 1 0 20,16",
        FileAction.Extract => "M4,7 L12,3 L20,7 M4,7 V20 H20 V7 M12,8 V16 M8,12 L12,16 L16,12",
        FileAction.CsvToXlsx => "M3,3 H21 V21 H3 Z M3,9 H21 M9,3 V21 M9,15 H21 M15,9 V21",
        _ => "M8,4 H5 V10 L2,12 L5,14 V20 H8 M16,4 H19 V10 L22,12 L19,14 V20 H16 M10,9 L14,15 M14,9 L10,15"
    };
}
