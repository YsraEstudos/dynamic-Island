using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

using UserControl = System.Windows.Controls.UserControl;
namespace Island.App.Budgets.Views;

public partial class BudgetView : UserControl
{
    private const double SlideDistance = 8;
    private static readonly Duration EnterDuration = new(TimeSpan.FromMilliseconds(180));

    public BudgetView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is not null)
            {
                PlayEnter();
            }
        };
    }

    /// <summary>Fade + slide curto (180 ms, 8 px) quando o orçamento exibido muda.</summary>
    private void PlayEnter()
    {
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        Root.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, EnterDuration) { EasingFunction = easing });
        SlideTransform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(SlideDistance, 0, EnterDuration) { EasingFunction = easing });
    }
}
