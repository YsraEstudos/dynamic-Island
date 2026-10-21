using Island.Windows.Input;

namespace Island.Windows.Tests;

public sealed class EscapeKeyWatcherTests
{
    private const uint Escape = EscapeKeyGate.VkEscape;

    [Fact]
    public void Unarmed_gate_passes_escape_through_and_reports_nothing()
    {
        var gate = new EscapeKeyGate();

        Assert.False(gate.Consume(Escape, isKeyUp: false, out bool pressed));
        Assert.False(pressed);
        Assert.False(gate.Consume(Escape, isKeyUp: true, out pressed));
        Assert.False(pressed);
    }

    [Fact]
    public void Armed_press_is_swallowed_once_with_its_repeats_and_release()
    {
        var gate = new EscapeKeyGate();
        gate.Arm();

        Assert.True(gate.Consume(Escape, isKeyUp: false, out bool pressed));
        Assert.True(pressed);
        Assert.True(gate.Consume(Escape, isKeyUp: false, out pressed));   // auto-repeat of the same press
        Assert.False(pressed);
        Assert.True(gate.Consume(Escape, isKeyUp: true, out pressed));
        Assert.False(pressed);

        Assert.True(gate.Consume(Escape, isKeyUp: false, out pressed));   // a new press is reported again
        Assert.True(pressed);
        Assert.True(gate.Consume(Escape, isKeyUp: true, out _));
    }

    [Fact]
    public void Release_of_a_swallowed_press_is_swallowed_even_after_disarming()
    {
        var gate = new EscapeKeyGate();
        gate.Arm();
        Assert.True(gate.Consume(Escape, isKeyUp: false, out _));

        gate.Disarm();

        Assert.True(gate.Consume(Escape, isKeyUp: false, out bool pressed));  // repeat still belongs to the swallowed press
        Assert.False(pressed);
        Assert.True(gate.Consume(Escape, isKeyUp: true, out _));
        Assert.False(gate.Consume(Escape, isKeyUp: false, out pressed));      // after disarming, presses pass through
        Assert.False(pressed);
        Assert.False(gate.Consume(Escape, isKeyUp: true, out _));
    }

    [Fact]
    public void Press_that_started_before_arming_is_never_swallowed_or_reported()
    {
        var gate = new EscapeKeyGate();
        Assert.False(gate.Consume(Escape, isKeyUp: false, out _));

        gate.Arm();

        Assert.False(gate.Consume(Escape, isKeyUp: false, out bool pressed));  // held key auto-repeat
        Assert.False(pressed);
        Assert.False(gate.Consume(Escape, isKeyUp: true, out _));
    }

    [Fact]
    public void Other_keys_are_never_swallowed_or_reported()
    {
        var gate = new EscapeKeyGate();
        gate.Arm();

        Assert.False(gate.Consume(0x0D, isKeyUp: false, out bool pressed));   // Enter
        Assert.False(pressed);
        Assert.False(gate.Consume(0x20, isKeyUp: true, out _));               // Space
    }

    [Fact]
    public void Watcher_start_and_dispose_are_idempotent_and_do_not_throw()
    {
        var watcher = new EscapeKeyWatcher(() => { });

        bool started = watcher.Start();
        Assert.Equal(started, watcher.Start());
        watcher.Dispose();
        watcher.Dispose();

        Assert.False(watcher.Start());
    }
}
