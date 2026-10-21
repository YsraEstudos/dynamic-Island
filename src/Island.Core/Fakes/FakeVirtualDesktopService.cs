using Island.Core.Abstractions;

namespace Island.Core.Fakes;

/// <summary>
/// In-memory desktops for tests and demo mode. <see cref="UserSwitchTo"/> plays the user switching desktops by hand;
/// <see cref="SwitchTo"/> is what the app does. Not thread-safe: tests drive it from one thread.
/// </summary>
public sealed class FakeVirtualDesktopService : IVirtualDesktopService
{
    private readonly List<VirtualDesktopInfo> _desktops = new();
    private Guid? _current;

    public FakeVirtualDesktopService(int count = 3, bool available = true)
    {
        for (int i = 0; i < count; i++) _desktops.Add(new VirtualDesktopInfo(Guid.NewGuid(), $"Área de trabalho {i + 1}", i));
        _current = _desktops.Count > 0 ? _desktops[0].Id : null;
        IsAvailable = available;
    }

    public bool IsAvailable { get; set; }

    /// <summary>When false, <see cref="GetCurrentDesktopId"/> returns null, as if the shell could not be asked.</summary>
    public bool CurrentReadable { get; set; } = true;

    /// <summary>Number of successful <see cref="SwitchTo"/> calls.</summary>
    public int SwitchCount { get; private set; }

    public IReadOnlyList<VirtualDesktopInfo> GetDesktops() => IsAvailable ? _desktops.ToArray() : Array.Empty<VirtualDesktopInfo>();

    public Guid? GetCurrentDesktopId() => IsAvailable && CurrentReadable ? _current : null;

    public bool SwitchTo(Guid desktopId)
    {
        if (!IsAvailable || _desktops.All(d => d.Id != desktopId)) return false;
        _current = desktopId;
        SwitchCount++;
        return true;
    }

    /// <summary>Simulates the user switching to a desktop by hand. Does not count as a switch of the app.</summary>
    public void UserSwitchTo(Guid desktopId) => _current = desktopId;

    /// <summary>Id of the desktop at <paramref name="index"/>.</summary>
    public Guid IdAt(int index) => _desktops[index].Id;
}
