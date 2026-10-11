using Island.App.ViewModels;

namespace Island.App.Views.Widgets;

/// <summary>
/// Shelf card with the Suite's running and recently finished tasks: up to three rows (title, a thin bar, the
/// percentage and the detail). It only binds to <see cref="ProgressPresenter"/>, which keeps no timer of its own.
/// </summary>
public partial class TasksWidget : System.Windows.Controls.UserControl
{
    public TasksWidget(IslandViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        InitializeComponent();
        DataContext = vm.TaskProgress;
    }
}
