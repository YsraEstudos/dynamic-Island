namespace Island.Core.Phone;

/// <summary>What the island knows about the current phone block.</summary>
public enum PhoneBlockStatus
{
    /// <summary>No block was sent (or the session ended).</summary>
    Idle,
    /// <summary>The FCM send was accepted; the island is waiting for the phone's ack.</summary>
    Awaiting,
    /// <summary>The phone confirmed a block (<c>applied &amp;&amp; a11y</c>).</summary>
    Confirmed,
    /// <summary>The phone answered that it could not apply the block, or no ack arrived in time.</summary>
    Failed,
}

/// <summary>
/// Tracks one phone block attempt so the island only shows "Celular bloqueado" after the phone really confirmed it.
/// The FCM HTTP 2xx only proves Google accepted the message, so the status starts as <see cref="PhoneBlockStatus.Awaiting"/>
/// and moves to confirmed/failed from an ack with the same message id. Acks for another id (an older block) are ignored.
/// A confirmed ack repeated later is the phone's 60 s heartbeat: it keeps the seal and is reported as relevant again.
/// A heartbeat that reports failure downgrades a confirmed block. Pure state machine: no clock or thread is held.
/// </summary>
public sealed class PhoneBlockTracker(Func<DateTimeOffset>? clock = null)
{
    /// <summary>How long the island waits for the phone's ack before showing "NÃO aplicado".</summary>
    public static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(20);

    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);
    private DateTimeOffset _expectUntil;

    public PhoneBlockStatus Status { get; private set; } = PhoneBlockStatus.Idle;

    /// <summary>FCM message id of the block being tracked; null when idle.</summary>
    public string? MessageId { get; private set; }

    /// <summary>Local end of the block, updated by each ack that carries a later end.</summary>
    public DateTimeOffset End { get; private set; }

    /// <summary>The last ack that belonged to this block, heartbeat included.</summary>
    public PhoneBlockAck? Ack { get; private set; }

    /// <summary>When the last relevant ack arrived.</summary>
    public DateTimeOffset? LastAckAt { get; private set; }

    /// <summary>User-facing reason while <see cref="Status"/> is failed; null otherwise.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>Starts waiting for the ack of <paramref name="messageId"/>. Replaces any previous block.</summary>
    public void Expect(string messageId, DateTimeOffset end, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        MessageId = messageId;
        End = end;
        Status = PhoneBlockStatus.Awaiting;
        Ack = null;
        LastAckAt = null;
        FailureReason = null;
        _expectUntil = _clock() + (timeout ?? AckTimeout);
    }

    /// <summary>
    /// Applies an ack. Returns false when it belongs to another block (or there is none). <paramref name="changed"/> is
    /// true when the status or the failure reason changed; a heartbeat for a confirmed block keeps it confirmed.
    /// Two kinds of ack are relevant while a block is expected: one with the exact FCM message id (the main app's
    /// confirmation) and a guardian heartbeat (<see cref="PhoneBlockAck.IsGuardianStatus"/>), which proves the phone is
    /// alive and its accessibility service is on even though the guardian never knows the message id. A heartbeat that
    /// reports failure from either end fails the block, just like an app ack.
    /// </summary>
    public bool Observe(PhoneBlockAck ack, out bool changed)
    {
        ArgumentNullException.ThrowIfNull(ack);
        changed = false;
        if (MessageId is null) return false;

        bool matchesMessage = string.Equals(ack.Id, MessageId, StringComparison.Ordinal);
        if (!matchesMessage && !ack.IsGuardianStatus) return false;

        Ack = ack;
        LastAckAt = _clock();
        if (ack.End is { } end && end > End) End = end;

        string? reason = ack.FailureReason();
        if (reason is null)
        {
            changed = Status != PhoneBlockStatus.Confirmed;
            Status = PhoneBlockStatus.Confirmed;
            FailureReason = null;
        }
        else
        {
            // A late failing heartbeat must be able to revoke a seal that was previously confirmed.
            changed = Status != PhoneBlockStatus.Failed || !string.Equals(FailureReason, reason, StringComparison.Ordinal);
            Status = PhoneBlockStatus.Failed;
            FailureReason = reason;
        }
        return true;
    }

    /// <summary>
    /// Fails an awaiting block once <see cref="AckTimeout"/> passed without a relevant ack. Returns true when this call
    /// changed the status (the caller shows the failure notice once).
    /// </summary>
    public bool CheckTimeout()
    {
        if (Status != PhoneBlockStatus.Awaiting || _clock() < _expectUntil) return false;
        Status = PhoneBlockStatus.Failed;
        FailureReason = "sem confirmação do celular";
        return true;
    }

    /// <summary>True while a confirmed block should still be shown (confirmed and within <paramref name="grace"/> of its end).</summary>
    public bool IsSealLive(TimeSpan grace) => Status == PhoneBlockStatus.Confirmed && _clock() <= End + grace;

    /// <summary>Forgets the current block, for example when the Angry session ends.</summary>
    public void Reset()
    {
        Status = PhoneBlockStatus.Idle;
        MessageId = null;
        Ack = null;
        LastAckAt = null;
        FailureReason = null;
        End = default;
    }
}
