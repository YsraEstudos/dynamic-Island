namespace Island.Core.Abstractions;

/// <summary>Result of asking the phone to block. <see cref="Detail"/> is a short message fit for a toast.</summary>
public readonly record struct PhoneBlockOutcome(bool Delivered, string Detail);

/// <summary>
/// Tells the phone (Foco &amp; Bem-Estar app) to block itself for a while. One tiny push per call, no polling,
/// nothing held open between calls. Never throws: failures come back as an outcome.
/// </summary>
public interface IPhoneBlockNotifier
{
    /// <param name="duration">How long the phone stays blocked, counted from now.</param>
    /// <param name="fcmToken">FCM registration token of the phone.</param>
    Task<PhoneBlockOutcome> NotifyAsync(TimeSpan duration, string fcmToken, CancellationToken ct = default);
}
