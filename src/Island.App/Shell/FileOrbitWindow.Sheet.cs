using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Island.Core.Files;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Path = System.Windows.Shapes.Path;

namespace Island.App.Shell;

public partial class FileOrbitWindow
{
    private static Color AccentFor(FileAction action) => (Color)System.Windows.Media.ColorConverter.ConvertFromString(action switch
    {
        FileAction.Images or FileAction.Video or FileAction.Audio => "#6C8DFF",
        FileAction.Index => "#FFB347", FileAction.Package or FileAction.Extract => "#B57CFF",
        FileAction.Rename => "#FF7AA2", FileAction.Duplicates => "#A66BFF",
        FileAction.ImagesPdf or FileAction.OfficePdf or FileAction.MergePdf or FileAction.SplitPdf or FileAction.CompressPdf => "#FF8A5C",
        _ => "#4FD1D9"
    });

    private void InitializeSheet()
    {
        DoneButton.Click += (_, _) => Close();
        LoadBatchPreview();
        SheetCloseButton.Click += (_, _) => Close();
        AnotherActionButton.Click += (_, _) => ShowActionList();
        WidthSlider.ValueChanged += (_, _) => WidthInput.Text = ((int)WidthSlider.Value).ToString();
        QualitySlider.ValueChanged += (_, _) => QualityInput.Text = ((int)QualitySlider.Value).ToString();
        WidthInput.TextChanged += (_, _) => { if (int.TryParse(WidthInput.Text, out int n) && n is >= 16 and <= 8192) WidthSlider.Value = n; };
        QualityInput.TextChanged += (_, _) => { if (int.TryParse(QualityInput.Text, out int n) && n is >= 10 and <= 100) QualitySlider.Value = n; };
        foreach (var action in FileActionCatalog.For(_paths))
        {
            var color = AccentFor(action);
            var icon = new Path { Data = Geometry.Parse(FileOrbitActionVisuals.GeometryFor(action)), Width = 22, Height = 22,
                Stretch = Stretch.Uniform, Stroke = new SolidColorBrush(Color.FromRgb(11,12,16)), StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round };
            var stack = new StackPanel();
            stack.Children.Add(new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(color), Child = icon,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0,0,0,10) });
            stack.Children.Add(new TextBlock { Text = FileOrbitActionVisuals.Label(action), FontSize = 14, FontWeight = FontWeights.SemiBold });
            stack.Children.Add(new TextBlock { Text = HubDescription(action), FontSize = 10.5, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(138,144,168)), Margin = new Thickness(0,5,0,0) });
            var tile = new Button { Content = stack, Tag = action, Margin = new Thickness(5), Padding = new Thickness(12),
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top,
                ToolTip = Help(action), MinHeight = 120, Background = new SolidColorBrush(Color.FromArgb(12,255,255,255)) };
            System.Windows.Automation.AutomationProperties.SetName(tile, FileActionCatalog.Label(action));
            tile.Click += (_, _) => { SelectAction(action); AnimateSheet(OptionsPanel); };
            tile.RenderTransform = new TranslateTransform();
            tile.MouseEnter += (_, _) => { tile.BorderBrush = new SolidColorBrush(color); if (!_reduce) ((TranslateTransform)tile.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-4, TimeSpan.FromMilliseconds(220))); };
            tile.MouseLeave += (_, _) => { tile.BorderBrush = new SolidColorBrush(Color.FromRgb(48,50,63)); if (!_reduce) ((TranslateTransform)tile.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))); };
            ActionTiles.Children.Add(tile);
        }
    }

    private void SetAccent(FileAction action)
    {
        Resources["OrbitAccent"] = new SolidColorBrush(AccentFor(action));
        Shell.BorderBrush = new SolidColorBrush(Color.FromArgb(70, AccentFor(action).R, AccentFor(action).G, AccentFor(action).B));
    }

    private void BuildFormatSegments()
    {
        FormatSegments.Children.Clear();
        foreach (string format in FormatInput.Items)
        {
            var button = new Button { Content = format.ToUpperInvariant(), Tag = format, Padding = new Thickness(5,10,5,10), FontSize = 11, Margin = new Thickness(2) };
            button.Click += (_, _) => { FormatInput.SelectedItem = format; RefreshSegments(); };
            FormatSegments.Children.Add(button);
        }
        RefreshSegments();
    }

    private void RefreshSegments()
    {
        foreach (Button button in FormatSegments.Children)
        {
            bool on = Equals(button.Tag, FormatInput.SelectedItem);
            button.Background = on ? new LinearGradientBrush(AccentFor(_selected!.Value), Color.FromRgb(163,157,255), 25) : new SolidColorBrush(Color.FromRgb(25,27,37));
            button.Foreground = on ? new SolidColorBrush(Color.FromRgb(11,12,16)) : new SolidColorBrush(Color.FromRgb(181,186,206));
            button.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void ShowActionList()
    {
        if (IsRunning) return;
        _selected = null;
        SetDetailsPresentation(true);
        OrbitPanel.Visibility = OptionsPanel.Visibility = ResultPanel.Visibility = DestinationPanel.Visibility = RunButton.Visibility = OpenButton.Visibility = DoneButton.Visibility = Visibility.Collapsed;
        ActionListPanel.Visibility = Visibility.Visible;
        TrayButton.Visibility = Visibility.Visible;
        StatusLabel.Text = "Escolha uma ação para configurar o lote.";
        Heading.Text = "O que vamos fazer?";
        Resources["OrbitAccent"] = new SolidColorBrush(Color.FromRgb(124,140,255));
        AnimateSheet(ActionListPanel);
    }

    private void AnimateSheet(FrameworkElement panel)
    {
        if (_reduce) return;
        panel.RenderTransformOrigin = new System.Windows.Point(.5,0);
        var scale = new ScaleTransform(); var slide = new TranslateTransform();
        panel.RenderTransform = new TransformGroup { Children = { scale, slide } };
        var duration = TimeSpan.FromMilliseconds(650);
        var ease = new BackEase { Amplitude = .35, EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.96,1,duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.9,1,duration) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-18,0,duration) { EasingFunction = ease });
        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(350)));
    }

    private void ShowWork()
    {
        OptionsPanel.Visibility = DestinationPanel.Visibility = RunButton.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        AnotherActionButton.Visibility = Visibility.Collapsed;
        DoneButton.Visibility = Visibility.Collapsed;
        ResultTitle.Text = "Processando…"; ResultSymbol.Text = "…"; ResultDetail.Text = BatchLabel.Text;
        ProgressArc.Visibility = Visibility.Visible;
        if (!_reduce)
        {
            var rotate = new RotateTransform(); ProgressArc.RenderTransform = rotate;
            rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0,360,TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        AnimateSheet(ResultPanel);
    }

    private void FinishWork(string title, string symbol, string detail)
    {
        if (ProgressArc.RenderTransform is RotateTransform rotate) rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        ProgressArc.Visibility = Visibility.Collapsed;
        ResultTitle.Text = title; ResultSymbol.Text = symbol; ResultDetail.Text = detail;
        AnotherActionButton.Visibility = Visibility.Visible;
        DoneButton.Visibility = Visibility.Visible;
    }

    private void LoadBatchPreview()
    {
        long size = 0; bool folders = false;
        foreach (string path in _paths)
        {
            try { if (System.IO.File.Exists(path)) size += new System.IO.FileInfo(path).Length; else folders = true; }
            catch (System.IO.IOException) { folders = true; }
            catch (UnauthorizedAccessException) { folders = true; }
        }
        BatchMetadata.Text = $"{_paths.Count} {(_paths.Count == 1 ? "item" : "itens")}" + (folders ? " · inclui pastas ou itens indisponíveis" : $" · {size / 1024d:N1} KB");
        try
        {
            string path = _paths[0];
            if (!System.IO.File.Exists(path) || new System.IO.FileInfo(path).Length > 64 * 1024 * 1024) return;
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif")) return;
            using var stream = System.IO.File.OpenRead(path);
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 96; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            PreviewImage.Source = bitmap; PreviewFallback.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or NotSupportedException or System.IO.FileFormatException or ArgumentException) { }
    }
}
