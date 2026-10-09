using Island.App.Budgets;
using Island.App.Widgets;
using Island.Core.Budgets;

namespace Island.App.Shell;

/// <summary>Keeps a single budget editor window alive at a time.</summary>
public sealed class BudgetWindowManager(BudgetBook book) : IBudgetWindowHost
{
    private BudgetWindow? _window;

    public void Open()
    {
        if (_window is null)
        {
            var window = new BudgetWindow(new BudgetsViewModel(book));
            window.Closed += (_, _) => { if (ReferenceEquals(_window, window)) _window = null; };
            _window = window;
        }

        _window.ShowOrActivate();
    }

    public void CloseForShutdown()
    {
        BudgetWindow? window = _window;
        _window = null;
        window?.Close();
    }
}
