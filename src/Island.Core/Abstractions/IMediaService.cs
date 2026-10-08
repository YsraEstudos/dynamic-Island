using Island.Core.Models;

namespace Island.Core.Abstractions;

public interface IMediaService : IDisposable
{
    /// <summary>Raised on any change. Argument is null when there is no media session. Raised from arbitrary threads.</summary>
    event EventHandler<MediaInfo?>? MediaChanged;

    MediaInfo? Current { get; }

    Task InitializeAsync(CancellationToken ct = default);
    Task PlayPauseAsync();
    Task NextAsync();
    Task PreviousAsync();
    Task SeekAsync(TimeSpan position);
}
