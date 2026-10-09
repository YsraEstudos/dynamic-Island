namespace Island.Core.Capture;

public enum RecordingPhase
{
    Idle,
    Starting,
    Recording,
    Stopping,
}

/// <summary>
/// Recording lifecycle: Idle -> Starting -> Recording -> Stopping -> Idle. Each Begin method returns false when the
/// transition is not allowed, so a double click or a hotkey during a transition is ignored. Not thread-safe; the
/// owner serializes access.
/// </summary>
public sealed class RecordingStateMachine
{
    public RecordingPhase Phase { get; private set; } = RecordingPhase.Idle;

    public bool IsRecording => Phase == RecordingPhase.Recording;

    public bool BeginStart()
    {
        if (Phase != RecordingPhase.Idle) return false;
        Phase = RecordingPhase.Starting;
        return true;
    }

    public void StartSucceeded()
    {
        if (Phase == RecordingPhase.Starting) Phase = RecordingPhase.Recording;
    }

    public void StartFailed()
    {
        if (Phase == RecordingPhase.Starting) Phase = RecordingPhase.Idle;
    }

    public bool BeginStop()
    {
        if (Phase != RecordingPhase.Recording) return false;
        Phase = RecordingPhase.Stopping;
        return true;
    }

    public void StopCompleted()
    {
        if (Phase == RecordingPhase.Stopping) Phase = RecordingPhase.Idle;
    }

    /// <summary>The capture backend ended the recording by itself. Returns true when that changed the phase.</summary>
    public bool EndedByBackend()
    {
        if (Phase != RecordingPhase.Recording) return false;
        Phase = RecordingPhase.Idle;
        return true;
    }
}
