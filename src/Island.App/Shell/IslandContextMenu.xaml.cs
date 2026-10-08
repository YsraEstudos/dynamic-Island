using System.Windows;
using System.Windows.Controls;
using Island.App.ViewModels;

namespace Island.App.Shell;

/// <summary>
/// Dark rounded menu opened by right-clicking the island: Customize Shelf, Open Clipboard, Minimize, Reset position,
/// Install update, Open Settings, Quit. The island window opens and closes it and reports its state, so a temporary
/// island state stays open under it.
/// </summary>
public partial class IslandContextMenu : System.Windows.Controls.ContextMenu
{
    public IslandContextMenu()
    {
        InitializeComponent();
    }

    /// <summary>"Open Settings" was chosen.</summary>
    public event Action? SettingsRequested;

    /// <summary>"Quit" was chosen.</summary>
    public event Action? QuitRequested;

    /// <summary>"Reset position" was chosen.</summary>
    public event Action? ResetPositionRequested;

    /// <summary>"Check for updates" was chosen.</summary>
    public event Action? CheckUpdateRequested;

    /// <summary>"Install update" was chosen.</summary>
    public event Action? InstallUpdateRequested;

    /// <summary>Connects the shelf and clipboard commands and the window-level actions.</summary>
    public void Bind(IslandViewModel vm)
    {
        CustomizeItem.Command = vm.OpenCustomizeCommand;
        ClipboardMenuItem.Command = vm.OpenClipboardCommand;
        MinimizeItem.Command = vm.MinimizeCommand;
        ResetPositionItem.Click += (_, _) => ResetPositionRequested?.Invoke();
        CheckUpdateItem.Click += (_, _) => CheckUpdateRequested?.Invoke();
        UpdateItem.Click += (_, _) => InstallUpdateRequested?.Invoke();
        SettingsItem.Click += (_, _) => SettingsRequested?.Invoke();
        QuitItem.Click += (_, _) => QuitRequested?.Invoke();
    }

    /// <summary>"Open Clipboard" is hidden when the clipboard feature is disabled in settings.</summary>
    public void SetClipboardAvailable(bool available) =>
        ClipboardMenuItem.Visibility = available ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    /// <summary>"Minimize" is hidden while the island is already the Mini pill.</summary>
    public void SetMinimizeAvailable(bool available) =>
        MinimizeItem.Visibility = available ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    /// <summary>"Reset position" is shown only while the island is at a dragged position.</summary>
    public void SetResetAvailable(bool available) =>
        ResetPositionItem.Visibility = available ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    /// <summary>"Install update" is shown only when a newer release is known; otherwise "Check for updates" takes its place. <paramref name="version"/> goes into the label, e.g. "v0.2.0".</summary>
    public void SetUpdateAvailable(string? version)
    {
        UpdateItem.Header = version is null ? "Install update" : $"Install update ({version})";
        UpdateItem.Visibility = version is null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        CheckUpdateItem.Visibility = version is null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }
}
