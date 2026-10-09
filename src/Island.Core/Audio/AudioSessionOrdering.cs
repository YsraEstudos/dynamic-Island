namespace Island.Core.Audio;

/// <summary>
/// Display order for the mixer: the foreground app first, then sessions that are playing, then the rest. Within a group
/// the order is by name, so rows do not swap places while peaks move. Only the active flag and the foreground decide the
/// order, never the peak level.
/// </summary>
public static class AudioSessionOrdering
{
    /// <param name="sessions">Sessions to order.</param>
    /// <param name="foregroundProcessId">Process id of the foreground window, or 0 when unknown.</param>
    public static IReadOnlyList<AppAudioSession> Order(IEnumerable<AppAudioSession> sessions, int foregroundProcessId)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        return sessions
            .OrderBy(s => foregroundProcessId != 0 && s.ProcessId == foregroundProcessId ? 0 : 1)
            .ThenBy(s => s.Active ? 0 : 1)
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(s => s.ProcessId)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();
    }
}
