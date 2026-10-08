using Island.Core.Models;

namespace Island.Core.Abstractions;

/// <summary>Passive operating-system signals rendered through the island's temporary notice flow.</summary>
public interface ISystemNoticeSource : IDisposable
{
    event EventHandler<Notice>? NoticeRaised;

    /// <summary>Starts observing changes. Initial state is recorded silently; repeated calls do nothing.</summary>
    void Start();
}
