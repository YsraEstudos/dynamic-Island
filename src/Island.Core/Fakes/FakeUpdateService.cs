using Island.Core.Abstractions;
using Island.Core.Models;

namespace Island.Core.Fakes;

/// <summary>Demo/test service: never finds an update and never touches the network.</summary>
public sealed class FakeUpdateService : IUpdateService
{
    public UpdateInfo? Available => null;

    public void Start()
    {
    }

    public Task<UpdateInfo?> CheckAsync(CancellationToken ct = default) => Task.FromResult<UpdateInfo?>(null);

    public Task InstallAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
}
