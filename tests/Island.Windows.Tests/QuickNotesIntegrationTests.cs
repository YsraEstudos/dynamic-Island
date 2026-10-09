using Island.App.Shell;
using Island.Windows.Input;

namespace Island.Windows.Tests;

public sealed class QuickNotesIntegrationTests
{
    [Fact]
    public void Disabled_hotkey_is_not_registered()
    {
        var fake = new FakeGlobalHotkey();
        int factoryCalls = 0;
        using var controller = new QuickNotesHotkeyController(
            () => { factoryCalls++; return fake; },
            () => { },
            action => action(),
            _ => { });

        controller.SetEnabled(false);

        Assert.Equal(0, factoryCalls);
        Assert.Equal(0, fake.RegisterCalls);
        Assert.False(controller.HotkeyConflict);
    }

    [Fact]
    public void Registration_conflict_reports_unavailable_without_throwing()
    {
        var fake = new FakeGlobalHotkey { RegisterResult = false };
        bool? reportedConflict = null;
        using var controller = new QuickNotesHotkeyController(
            () => fake,
            () => { },
            action => action(),
            conflict => reportedConflict = conflict);

        controller.SetEnabled(true);

        Assert.False(controller.IsAvailable);
        Assert.True(controller.HotkeyConflict);
        Assert.True(reportedConflict);
        Assert.True(fake.Disposed);
    }

    [Fact]
    public void Hotkey_press_is_dispatched_to_the_capture_callback()
    {
        var fake = new FakeGlobalHotkey();
        Action? queuedUiAction = null;
        int captureRequests = 0;
        using var controller = new QuickNotesHotkeyController(
            () => fake,
            () => captureRequests++,
            action => queuedUiAction = action,
            _ => { });
        controller.SetEnabled(true);

        fake.RaisePressed();

        Assert.NotNull(queuedUiAction);
        Assert.Equal(0, captureRequests);
        queuedUiAction!();
        Assert.Equal(1, captureRequests);
    }

    [Fact]
    public void Disabling_hotkey_disposes_the_previous_registration()
    {
        var fake = new FakeGlobalHotkey();
        using var controller = new QuickNotesHotkeyController(
            () => fake,
            () => { },
            action => action(),
            _ => { });

        controller.SetEnabled(true);
        controller.SetEnabled(false);

        Assert.True(fake.Disposed);
        Assert.False(controller.HotkeyConflict);
        Assert.False(controller.IsEnabled);
    }

    private sealed class FakeGlobalHotkey : IGlobalHotkey
    {
        public event Action? Pressed;
        public bool RegisterResult { get; set; } = true;
        public int RegisterCalls { get; private set; }
        public bool Disposed { get; private set; }

        public bool Register()
        {
            RegisterCalls++;
            return RegisterResult;
        }

        public void Dispose() => Disposed = true;
        public void RaisePressed() => Pressed?.Invoke();
    }
}
