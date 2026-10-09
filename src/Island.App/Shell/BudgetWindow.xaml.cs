using System.Windows;
using Island.App.Budgets;

namespace Island.App.Shell;

/// <summary>Editor for the AI budgets shown by the shelf widget.</summary>
public partial class BudgetWindow : Window
{
    private readonly BudgetsViewModel _viewModel;

    public BudgetWindow(BudgetsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Activated += (_, _) => _viewModel.Refresh();
        Closed += (_, _) => _viewModel.Detach();
    }

    public void ShowOrActivate()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
}
