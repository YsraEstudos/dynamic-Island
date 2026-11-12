namespace Island.Core.Abstractions;

/// <summary>
/// Result of asking the phone to block. <see cref="Delivered"/> only means the FCM API accepted the message: the phone
/// has not confirmed anything yet. <see cref="MessageId"/> and <see cref="End"/> identify the sent block so its ack can
/// be matched (see <see cref="Island.Core.Phone.PhoneBlockAck"/>); both are null when nothing was sent. <see cref="Detail"/> is a
/// short message fit for a toast.
/// </summary>
public readonly record struct PhoneBlockOutcome(bool Delivered, string Detail, string? MessageId = null,
    DateTimeOffset? End = null);

/// <summary>
/// Tells the phone (Foco &amp; Bem-Estar app) to block itself for a while. One tiny push per call, no polling,
/// nothing held open between calls. Never throws: failures come back as an outcome.
/// </summary>
public interface IPhoneBlockNotifier
{
    /// <param name="duration">How long the phone stays fully blocked, counted from now.</param>
    /// <param name="fcmToken">FCM registration token of the phone.</param>
    /// <param name="adultDuration">
    /// How long the phone keeps adult content blocked, counted from now, when it is longer than <paramref name="duration"/>.
    /// Zero (the default) or a shorter span sends no separate adult block.
    /// </param>
    /// <param name="ct">Stops pending retries.</param>
    Task<PhoneBlockOutcome> NotifyAsync(TimeSpan duration, string fcmToken, TimeSpan adultDuration = default, CancellationToken ct = default);
}
