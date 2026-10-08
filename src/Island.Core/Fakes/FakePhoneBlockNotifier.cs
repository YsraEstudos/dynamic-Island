using Island.Core.Abstractions;

namespace Island.Core.Fakes;

/// <summary>Demo/test notifier: records the last request and reports success without touching the network.</summary>
public sealed class FakePhoneBlockNotifier : IPhoneBlockNotifier
{
    public TimeSpan? LastDuration { get; private set; }
    public int Calls { get; private set; }

    public Task<PhoneBlockOutcome> NotifyAsync(TimeSpan duration, string fcmToken, CancellationToken ct = default)
    {
        Calls++;
        LastDuration = duration;
        return Task.FromResult(new PhoneBlockOutcome(true, "demo"));
    }
}
