using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Island.App.Budgets;

using UserControl = System.Windows.Controls.UserControl;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
namespace Island.App.Budgets.Views;

public partial class BudgetEditor : UserControl
{
    private static readonly int[] Hours = Enumerable.Range(0, 24).ToArray();

    public BudgetEditor()
    {
        InitializeComponent();
        HourBox.ItemsSource = Hours;
        DataContextChanged += OnBudgetChanged;
    }

    /// <summary>Recolhido se já houver dados; aberto numa IA recém-criada (nada digitado ainda).</summary>
    private void OnBudgetChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is BudgetViewModel budget)
        {
            Editor.IsExpanded = budget.Model.EnteredValue == 0 && budget.Model.ExcludedDays.Count == 0;
        }
    }

    /// <summary>Enter confirma o campo (LostFocus atualiza o binding) e avança o foco.</summary>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.OriginalSource is not UIElement element)
        {
            return;
        }

        element.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        e.Handled = true;
    }
}
