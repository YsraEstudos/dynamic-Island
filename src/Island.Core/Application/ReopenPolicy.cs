using Island.Core.Models;

namespace Island.Core.Application;

/// <summary>
/// Which panel a reopen from Compact or a temporary state shows: the panel that was shown last. That is the Clipboard
/// while the Clipboard is enabled, otherwise the shelf. The shelf keeps its row, so it returns to that row too.
/// </summary>
public static class ReopenPolicy
{
    public static IslandMode Target(IslandMode lastPanel, bool clipboardEnabled) =>
        lastPanel == IslandMode.Clipboard && clipboardEnabled ? IslandMode.Clipboard : IslandMode.Expanded;
}
