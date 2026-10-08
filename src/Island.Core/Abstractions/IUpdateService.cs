using Island.Core.Models;

namespace Island.Core.Abstractions;

/// <summary>
/// Finds and installs a newer build of the app. The background checks never throw; <see cref="CheckAsync"/> and
/// <see cref="InstallAsync"/> report failures by throwing, so the caller decides what the user sees.
/// </summary>
public interface IUpdateService
{
    /// <summary>The newer release found by the last successful check; null when up to date or nothing was found yet.</summary>
    UpdateInfo? Available { get; }

    /// <summary>Starts the checks: once shortly after startup, then periodically. Idempotent.</summary>
    void Start();

    /// <summary>Asks the update source for its latest release. Returns it when it is newer than the running app, otherwise null.</summary>
    Task<UpdateInfo?> CheckAsync(CancellationToken ct = default);

    /// <summary>Downloads the package and hands it to the installer. <paramref name="progress"/> reports 0..1 when the size is known.</summary>
    Task InstallAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default);
}
