using Island.Core.Audio;

namespace Island.Core.Abstractions;

/// <summary>Volume, mute and peak of each application audio session on the default output device.</summary>
public interface IAudioMixerService : IDisposable
{
    /// <summary>
    /// Raised when the session list, a volume, a mute, an activity flag or a peak changes. Raised from a background thread;
    /// subscribers marshal to their own thread and must not block.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>Latest published sessions, in no particular order (see <see cref="AudioSessionOrdering"/>). Safe from any thread.</summary>
    IReadOnlyList<AppAudioSession> Sessions { get; }

    /// <summary>Starts tracking sessions. Idempotent.</summary>
    void Start();

    /// <summary>Sets one session's volume (0-100). Unknown ids are ignored.</summary>
    void SetVolume(string sessionId, int level0to100);

    /// <summary>Mutes or unmutes one session. Unknown ids are ignored.</summary>
    void SetMuted(string sessionId, bool muted);

    /// <summary>
    /// Reads peaks while the returned scope is alive and at least one session has audio to show. Scopes nest; disposing
    /// the last one stops the reads. The widget holds one only while it is visible.
    /// </summary>
    IDisposable BeginMetering();
}
