using System.Windows;
using Island.App.ViewModels;
using Island.App.Views;
using Island.App.Views.Widgets;

namespace Island.App.Widgets;

/// <param name="IconKey">Key of a Geometry resource (24-unit icon) available app-wide, shown in the customize tray.</param>
/// <param name="Width">Preferred width in DIPs. All widgets share the same height (WidgetCatalog.WidgetHeight).</param>
public sealed record WidgetDescriptor(string Id, string Title, string IconKey, double Width,
    Func<ShelfContext, IslandViewModel, FrameworkElement> Create);

/// <summary>The shelf widgets. Every widget is WidgetHeight tall.</summary>
public static class WidgetCatalog
{
    public const double WidgetHeight = 152;

    public static IReadOnlyList<WidgetDescriptor> All { get; } = new[]
    {
        new WidgetDescriptor("nowplaying", "Now Playing", "Icon.Widget.NowPlaying", 400,
            (ctx, vm) =>
            {
                var widget = new MediaExpandedView { Width = 400, Height = WidgetHeight };
                widget.Attach(vm);
                return widget;
            }),
        new WidgetDescriptor("pomodoro", "Pomodoro", "Icon.Widget.Pomodoro", 300,
            (ctx, vm) => new PomodoroWidget(ctx)),
        new WidgetDescriptor("calendar", "Calendar", "Icon.Widget.Calendar", 280,
            (ctx, vm) => new CalendarWidget(ctx)),
        new WidgetDescriptor("quicknotes", "Notas", "Icon.Widget.QuickNotes", 340,
            (ctx, vm) => new QuickNotesWidget(ctx)),
        new WidgetDescriptor("gamenotes", "Notas do Jogo", "Icon.Widget.GameNotes", 280,
            (ctx, vm) => new GameNotesWidget(ctx)),
        new WidgetDescriptor("budget", "Orçamento IA", "Icon.Widget.Budget", 280,
            (ctx, vm) => new BudgetWidget(ctx)),
        new WidgetDescriptor("performance", "Desempenho", "Icon.Widget.Performance", 300,
            (ctx, vm) => new PerformanceWidget(ctx)),
        new WidgetDescriptor("filetray", "File Tray", "Icon.Widget.FileTray", 280,
            (ctx, vm) => new FileTrayWidget(ctx)),
        new WidgetDescriptor("capture", "Captura", "Icon.Widget.Capture", 280,
            (ctx, vm) => new CaptureWidget(ctx)),
        new WidgetDescriptor("mixer", "Mixer", "Icon.Widget.Mixer", 280,
            (ctx, vm) => new MixerWidget(ctx)),
        new WidgetDescriptor("calculator", "Calculadora", "Icon.Widget.Calculator", 250,
            (ctx, vm) => new CalculatorWidget(ctx)),
        new WidgetDescriptor("counter", "Contador", "Icon.Widget.Counter", 240,
            (ctx, vm) => new CounterWidget(ctx)),
        new WidgetDescriptor("textanalyzer", "Analisador de Texto", "Icon.Widget.TextAnalyzer", 320,
            (ctx, vm) => new TextAnalyzerWidget(ctx)),
        new WidgetDescriptor("timer", "Timer e Cronômetro", "Icon.Widget.Timer", 300,
            (ctx, vm) => new TimerWidget(ctx)),
        new WidgetDescriptor("focussounds", "Sons de Foco", "Icon.Widget.FocusSounds", 340,
            (ctx, vm) => new FocusSoundsWidget(ctx)),
        new WidgetDescriptor("tasks", "Tarefas", "Icon.Widget.Tasks", 300,
            (ctx, vm) => new TasksWidget(vm)),
    };

    public static WidgetDescriptor? Find(string id) => All.FirstOrDefault(w => w.Id == id);
}
