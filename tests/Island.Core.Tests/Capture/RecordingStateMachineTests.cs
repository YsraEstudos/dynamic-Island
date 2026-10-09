using Island.Core.Capture;

namespace Island.Core.Tests.Capture;

public sealed class RecordingStateMachineTests
{
    [Fact]
    public void Start_then_stop_walks_through_every_phase()
    {
        var machine = new RecordingStateMachine();

        Assert.True(machine.BeginStart());
        Assert.Equal(RecordingPhase.Starting, machine.Phase);
        machine.StartSucceeded();
        Assert.True(machine.IsRecording);
        Assert.True(machine.BeginStop());
        Assert.Equal(RecordingPhase.Stopping, machine.Phase);
        machine.StopCompleted();
        Assert.Equal(RecordingPhase.Idle, machine.Phase);
    }

    [Fact]
    public void A_second_start_is_ignored_while_starting_or_recording()
    {
        var machine = new RecordingStateMachine();

        Assert.True(machine.BeginStart());
        Assert.False(machine.BeginStart());
        machine.StartSucceeded();
        Assert.False(machine.BeginStart());
    }

    [Fact]
    public void Stop_is_ignored_unless_recording()
    {
        var machine = new RecordingStateMachine();

        Assert.False(machine.BeginStop());
        machine.BeginStart();
        Assert.False(machine.BeginStop());
    }

    [Fact]
    public void A_failed_start_returns_to_idle()
    {
        var machine = new RecordingStateMachine();

        machine.BeginStart();
        machine.StartFailed();

        Assert.Equal(RecordingPhase.Idle, machine.Phase);
    }

    [Fact]
    public void The_backend_ending_a_recording_returns_to_idle_once()
    {
        var machine = new RecordingStateMachine();
        machine.BeginStart();
        machine.StartSucceeded();

        Assert.True(machine.EndedByBackend());
        Assert.False(machine.EndedByBackend());
        Assert.Equal(RecordingPhase.Idle, machine.Phase);
    }

    [Fact]
    public void The_backend_ending_is_ignored_during_a_normal_stop()
    {
        var machine = new RecordingStateMachine();
        machine.BeginStart();
        machine.StartSucceeded();
        machine.BeginStop();

        Assert.False(machine.EndedByBackend());
        Assert.Equal(RecordingPhase.Stopping, machine.Phase);
    }
}
