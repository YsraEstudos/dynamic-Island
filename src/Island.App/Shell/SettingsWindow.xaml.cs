using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Island.App.ViewModels;

namespace Island.App.Shell;

/// <summary>
/// Settings window. Closing it (title bar, Alt+F4, Esc) only hides it, so the app keeps running
/// and the same instance can be shown again via <see cref="ShowOrActivate"/>.
/// </summary>
public partial class SettingsWindow : Window
{
    private bool _isShuttingDown;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;

        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Hide();
            }
        };
    }

    private async void OnCheckForUpdates(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) await vm.CheckForUpdatesAsync();
    }

    private async void OnSendPhoneTest(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        var answer = System.Windows.MessageBox.Show(this,
            "This blocks your phone for 1 minute. You can end it early by typing the phrase on the phone.\n\nSend the test?",
            "Dynamic Island", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes) await vm.SendPhoneTestAsync();
    }

    /// <summary>Shows the window if hidden, restores it if minimized, and brings it to the foreground.</summary>
    public void ShowOrActivate()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Focus();
    }

    /// <summary>Really closes the window (use on application shutdown only).</summary>
    public void CloseForShutdown()
    {
        _isShuttingDown = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (!_isShuttingDown)
        {
            e.Cancel = true;
            Hide();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TryEnableDarkTitleBar();
    }

    private void TryEnableDarkTitleBar()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int useDark = 1;

            // DWMWA_USE_IMMERSIVE_DARK_MODE is 20 on Windows 10 2004+ and 19 on earlier builds.
            if (DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, 19, ref useDark, sizeof(int));
            }
        }
        catch (DllNotFoundException)
        {
            // Not available: keep the default title bar.
        }
        catch (EntryPointNotFoundException)
        {
            // Not available: keep the default title bar.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
