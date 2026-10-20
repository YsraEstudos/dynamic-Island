using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Island.Core.Files;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;

namespace Island.App.Shell;

public partial class FileOrbitWindow : Window
{
    private readonly IReadOnlyList<string> _paths;
    private readonly bool _reduce;
    private FileAction? _selected;
    private CancellationTokenSource? _cancellation;
    private string _destination;
    private string? _results;
    private bool _closeAfterCancel;
    public bool IsRunning => _cancellation is not null;
    public Point RevealOrigin { get; set; } = new(260, 260);
    public event Action<IReadOnlyList<string>>? StoreRequested;

    public FileOrbitWindow(IReadOnlyList<string> paths, bool reduceAnimations)
    {
        InitializeComponent();
        _paths = paths; _reduce = reduceAnimations;
        _destination = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        DestinationLabel.Text = _destination;
        BatchLabel.Text = $"{paths.Count} {(paths.Count == 1 ? "item" : "itens")} · " + string.Join(", ", paths.Take(3).Select(Path.GetFileName));
        CenterCount.Text = paths.Count == 1 ? Path.GetFileName(paths[0]) : $"{paths.Count} itens · Escolha uma ação";
        string suggested = paths.Count == 1 ? Path.GetFileNameWithoutExtension(paths[0]) : "arquivos-" + DateTime.Now.ToString("yyyy-MM-dd");
        NameInput.Text = FileActionService.SafeName(suggested);
        BuildOrbit();
        CloseButton.Click += (_, _) => Close();
        OrbitCloseButton.Click += (_, _) => Close();
        OrbitTrayButton.Click += (_, _) => { StoreRequested?.Invoke(_paths); Close(); };
        AttachActionDrop(OrbitTrayButton, null);
        BackButton.Click += (_, _) => ShowOrbit();
        DestinationButton.Click += (_, _) => ChooseDestination();
        RunButton.Click += async (_, _) => await RunAction();
        CancelButton.Click += (_, _) => { _cancellation?.Cancel(); CancelButton.IsEnabled = false; StatusLabel.Text = "Cancelando…"; };
        OpenButton.Click += (_, _) => { if (_results is { } folder) OpenFolder(folder); };
        TrayButton.Click += (_, _) => { StoreRequested?.Invoke(_paths); Close(); };
        NameInput.TextChanged += (_, _) => UpdatePreview();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Closing += (_, e) =>
        {
            if (!IsRunning) { AnimateClose(e); return; }
            e.Cancel = true; _closeAfterCancel = true; _cancellation?.Cancel(); StatusLabel.Text = "Cancelando antes de fechar…";
        };
        Loaded += (_, _) => AnimateOrbit();
        Deactivated += (_, _) => { if (!IsDragPreview && _selected is null && !IsRunning) Close(); };
    }

    private void BuildOrbit()
    {
        var actions = FileActionCatalog.For(_paths);
        for (int i = 0; i < actions.Count; i++)
        {
            FileAction action = actions[i];
            string[] palette = ["#5B8CFF", "#A66BFF", "#14B8A6", "#F5A300", "#FF7A45", "#FF4D6D", "#36BFF1"];
            var button = new RadialSectorButton { Tag = action, Width = 520, Height = 520,
                Sector = RadialSectorButton.CreateSector(i, actions.Count), Angle = i * 360d / actions.Count,
                Accent = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette[i % palette.Length]),
                ToolTip = FileActionCatalog.Label(action) + "\n" + Help(action), Style = (Style)FindResource("OrbitActionStyle") };
            button.Content = FileOrbitActionVisuals.Create(button, action);
            System.Windows.Automation.AutomationProperties.SetName(button, FileActionCatalog.Label(action));
            Canvas.SetLeft(button, 0); Canvas.SetTop(button, 0);
            button.Click += (_, _) => PickAction(button, action);
            AttachActionDrop(button, action);
            button.MouseEnter += (_, _) => Hover(button, true);
            button.MouseLeave += (_, _) => Hover(button, false);
            button.GotKeyboardFocus += (_, _) => Hover(button, true);
            button.LostKeyboardFocus += (_, _) => { if (!button.IsMouseOver) Hover(button, false); };
            OrbitCanvas.Children.Add(button);
        }
    }

    private void SetDetailsPresentation(bool details)
    {
        ResizePresentation(details);
        Shell.Background = details ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 24, 19)) : null;
        Shell.BorderThickness = new Thickness(details ? 1 : 0);
        Shell.Margin = new Thickness(details ? 8 : 0);
        SheetGrid.Margin = details ? new Thickness(28, 22, 28, 22) : new Thickness(0);
        Header.Visibility = FooterPanel.Visibility = details ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string Help(FileAction action) => action switch
    {
        FileAction.Rename => "Cria cópias com nomes por data, sequência ou conteúdo de TXT, MD, CSV, JSON e LOG. Outros formatos usam o nome original em {content}.",
        FileAction.Duplicates => "Compara SHA-256 e, opcionalmente, aparência de imagens. Gera um relatório local; não apaga arquivos.",
        FileAction.Package => "Cria um ZIP pronto para compartilhar, mantendo a estrutura das pastas.",
        FileAction.Index => "Gera um índice HTML com nomes, caminhos, tamanhos, datas e miniaturas incorporadas das imagens.",
        FileAction.ContactSheet => "Organiza 12 imagens por página em um PDF A4, pronto para visualizar ou imprimir.",
        FileAction.Images => "Converte PNG, JPG, WebP e HEIC/HEIF para PNG, JPG ou WebP; limita a largura e ajusta a qualidade. O conversor de imagens já está incluído no aplicativo.",
        FileAction.ImagesPdf => "Uma imagem por página, na ordem do lote. Gera um PDF para compartilhar.",
        FileAction.OfficePdf => "Converte DOCX, PPTX e XLSX usando LibreOffice. Fontes e layout dependem da instalação local.",
        FileAction.MergePdf => "Junta as páginas na ordem exibida abaixo. PDFs protegidos podem exigir desbloqueio prévio.",
        FileAction.SplitPdf => "Cria um PDF separado para cada página de cada documento.",
        FileAction.CompressPdf => "Otimiza e reamostra imagens com Ghostscript. O ganho depende do PDF e pode haver perda visual.",
        FileAction.Video => "Converte e comprime MP4, MOV ou WebM, extrai MP3 e cria GIF ou prévia JPG. Use início e duração para delimitar o trecho.",
        FileAction.Audio => "Converte e comprime MP3, WAV, M4A e OGG. Para cortar, informe início e duração em segundos.",
        FileAction.Extract => "Extrai cada ZIP em sua própria pasta. Limite por arquivo: 20.000 entradas e 10 GB descompactados.",
        FileAction.CsvToXlsx => "Transforma CSV separado por vírgulas em planilha XLSX, preservando os valores como texto.",
        _ => "Converte um array JSON de objetos em CSV. Une as colunas; objetos aninhados permanecem como JSON nas células."
    };

    private void ShowOrbit()
    {
        if (IsRunning) return;
        _selected = null;
        SetDetailsPresentation(false);
        OrbitPanel.Visibility = Visibility.Visible;
        OptionsPanel.Visibility = DestinationPanel.Visibility = RunButton.Visibility = OpenButton.Visibility = Visibility.Collapsed;
        TrayButton.Visibility = Visibility.Visible;
        StatusLabel.Text = "Os originais ficam intactos. Você escolhe onde salvar.";
        AnimateOrbit();
    }

    private void SelectAction(FileAction action)
    {
        _selected = action;
        SetDetailsPresentation(true);
        OrbitPanel.Visibility = Visibility.Collapsed;
        OptionsPanel.Visibility = DestinationPanel.Visibility = RunButton.Visibility = Visibility.Visible;
        TrayButton.Visibility = OpenButton.Visibility = Visibility.Collapsed;
        ActionTitle.Text = FileActionCatalog.Label(action); ActionHelp.Text = Help(action);
        NameOptions.Visibility = action is FileAction.Rename or FileAction.Package ? Visibility.Visible : Visibility.Collapsed;
        NameLabel.Text = action == FileAction.Rename ? "Modelo dos nomes das cópias" : "Nome do pacote";
        RenameHelp.Visibility = action == FileAction.Rename ? Visibility.Visible : Visibility.Collapsed;
        if (action == FileAction.Rename) NameInput.Text = "{name}_{n:000}";
        else NameInput.Text = FileActionService.SafeName(_paths.Count == 1 ? Path.GetFileNameWithoutExtension(_paths[0]) : "arquivos-" + DateTime.Now.ToString("yyyy-MM-dd"));
        bool media = action is FileAction.Images or FileAction.Video or FileAction.Audio;
        FormatOptions.Visibility = media ? Visibility.Visible : Visibility.Collapsed;
        MediaOptions.Visibility = media || action is FileAction.ImagesPdf or FileAction.ContactSheet ? Visibility.Visible : Visibility.Collapsed;
        WidthField.Visibility = action == FileAction.Audio ? Visibility.Collapsed : Visibility.Visible;
        ImageWidthColumn.Width = action == FileAction.Audio ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        TrimOptions.Visibility = action is FileAction.Video or FileAction.Audio ? Visibility.Visible : Visibility.Collapsed;
        SimilarInput.Visibility = action == FileAction.Duplicates ? Visibility.Visible : Visibility.Collapsed;
        FormatInput.ItemsSource = action switch
        {
            FileAction.Video => new[] { "mp4", "mov", "webm", "mp3", "gif", "jpg" },
            FileAction.Audio => new[] { "mp3", "wav", "m4a", "ogg" },
            _ => new[] { "jpg", "png", "webp" }
        };
        FormatInput.SelectedIndex = 0;
        string? missing = FileTools.MissingFor(action);
        RunButton.IsEnabled = missing is null;
        StatusLabel.Text = missing ?? "Pronto. Os resultados serão salvos em uma pasta nova.";
        UpdatePreview();
        if (!_reduce)
        {
            var offset = new TranslateTransform(0, 14);
            OptionsPanel.RenderTransform = offset;
            offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            OptionsPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }
    }

    private void UpdatePreview()
    {
        if (_selected is null) return;
        try
        {
            InputPreview.Text = string.Join("\n", _paths.Take(4).Select((p, i) => _selected == FileAction.Rename && File.Exists(p)
                ? Path.GetFileName(p) + "  →  " + FileActionService.RenamePreview(p, i + 1, NameInput.Text) : p))
                + (_paths.Count > 4 ? $"\n+ {_paths.Count - 4} itens" : "");
        }
        catch (IOException ex) { InputPreview.Text = ex.Message; }
        catch (UnauthorizedAccessException ex) { InputPreview.Text = ex.Message; }
    }

    private void ChooseDestination()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Escolha onde criar a pasta de resultados", InitialDirectory = _destination };
        if (dialog.ShowDialog(this) == true) { _destination = dialog.FolderName; DestinationLabel.Text = _destination; }
    }

    private async Task RunAction()
    {
        if (_selected is not { } action || IsRunning) return;
        FileActionOptions options;
        try
        {
            int ParseInt(string text, int min, int max)
            {
                if (!int.TryParse(text, out int value) || value < min || value > max) throw new FormatException($"Informe um número inteiro entre {min} e {max}.");
                return value;
            }
            double ParseSeconds(string text)
            {
                if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || value < 0)
                    throw new FormatException("Início e duração devem ser números maiores ou iguais a zero.");
                return value;
            }
            options = new FileActionOptions
            {
                Name = NameInput.Text, RenamePattern = NameInput.Text, Format = FormatInput.SelectedItem as string ?? "jpg",
                MaxWidth = MediaOptions.Visibility == Visibility.Visible && WidthField.Visibility == Visibility.Visible ? ParseInt(WidthInput.Text, 16, 8192) : 1920,
                Quality = MediaOptions.Visibility == Visibility.Visible ? ParseInt(QualityInput.Text, 10, 100) : 80,
                StartSeconds = TrimOptions.Visibility == Visibility.Visible ? ParseSeconds(StartInput.Text) : 0,
                DurationSeconds = TrimOptions.Visibility == Visibility.Visible ? ParseSeconds(DurationInput.Text) : 0, FindSimilar = SimilarInput.IsChecked == true
            };
            if (action is FileAction.Rename or FileAction.Package && string.IsNullOrWhiteSpace(NameInput.Text)) throw new FormatException("Informe um nome ou modelo.");
        }
        catch (FormatException ex) { StatusLabel.Text = ex.Message; return; }
        string output = FileActionService.UniquePath(_destination, "Island-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        _cancellation = new CancellationTokenSource();
        OptionsPanel.IsEnabled = DestinationPanel.IsEnabled = RunButton.IsEnabled = false;
        WorkProgress.Visibility = CancelButton.Visibility = Visibility.Visible;
        CancelButton.IsEnabled = true; OpenButton.Visibility = Visibility.Collapsed;
        try
        {
            await new FileActionService().RunAsync(action, _paths, output, options,
                new Progress<string>(text => StatusLabel.Text = text), _cancellation.Token);
            _results = output;
            StatusLabel.Text = "Concluído · " + output;
            OpenButton.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "Cancelado. Os arquivos incompletos deste lote foram descartados.";
            if (Directory.Exists(output)) { _results = output; OpenButton.Visibility = Visibility.Visible; }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Não foi possível concluir: " + ex.Message;
            if (Directory.Exists(output)) { _results = output; OpenButton.Visibility = Visibility.Visible; }
        }
        finally
        {
            _cancellation.Dispose(); _cancellation = null;
            OptionsPanel.IsEnabled = DestinationPanel.IsEnabled = RunButton.IsEnabled = true;
            WorkProgress.Visibility = CancelButton.Visibility = Visibility.Collapsed;
            if (_closeAfterCancel) Close();
        }
    }

    private void OpenFolder(string folder)
    {
        try { Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
        catch (Exception ex) { StatusLabel.Text = "Não foi possível abrir a pasta: " + ex.Message; }
    }
}
