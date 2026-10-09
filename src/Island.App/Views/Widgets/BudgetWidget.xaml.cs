using System.Windows;
using System.Windows.Controls;
using Island.App.Budgets;
using Island.App.Widgets;
using Island.Core.Budgets;

using UserControl = System.Windows.Controls.UserControl;

namespace Island.App.Views.Widgets;

/// <summary>Shelf card: today's allowance for the selected AI, quick usage buttons and a switch between AIs.</summary>
public partial class BudgetWidget : UserControl
{
    private readonly BudgetBook _book;
    private readonly UiSignal _signal;
    private bool _subscribed;

    public BudgetWidget(ShelfContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        InitializeComponent();
        _book = context.Budgets ?? throw new InvalidOperationException("Budget book is not available.");
        _signal = new UiSignal(Dispatcher, Refresh);

        PrevButton.Click += () => _book.Cycle(-1);
        NextButton.Click += () => _book.Cycle(1);
        Use1Button.Click += () => AddUsage(1);
        Use5Button.Click += () => AddUsage(5);
        IBudgetWindowHost? host = context.BudgetHost;
        OpenButton.Click += () => host?.Open();
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }

    private void Subscribe()
    {
        if (_subscribed) return;

        _book.Changed += OnChanged;
        _subscribed = true;
        Refresh();
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;

        _book.Changed -= OnChanged;
        _subscribed = false;
    }

    private void OnChanged() => _signal.Signal();

    private void AddUsage(double percent)
    {
        if (_book.Selected is { } budget) _book.AddUsage(budget.Id, percent);
    }

    private void Refresh()
    {
        Visibility switcher = _book.Budgets.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        PrevButton.Visibility = switcher;
        NextButton.Visibility = switcher;

        if (_book.Selected is not { } budget)
        {
            NameText.Text = "Orçamento";
            HeroText.Text = "—";
            DetailText.Text = "Toque em Abrir para criar";
            RemainingText.Text = string.Empty;
            return;
        }

        BudgetPlan plan = _book.PlanOf(budget);
        NameText.Text = string.IsNullOrWhiteSpace(budget.Name) ? "IA" : budget.Name;
        HeroText.Text = BudgetTextFormatter.Hero(plan);
        DetailText.Text = Detail(budget, plan);
        RemainingText.Text = BudgetTextFormatter.Remaining(plan);
    }

    private static string Detail(Budget budget, BudgetPlan plan)
    {
        string status = BudgetTextFormatter.Status(budget, plan);
        if (status.Length > 0) return status;
        return $"Pode usar {BudgetTextFormatter.Allowance(plan)} hoje";
    }
}
