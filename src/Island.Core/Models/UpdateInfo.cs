namespace Island.Core.Models;

/// <summary>A newer build found on the update source. <see cref="Version"/> is normalised to four parts; <see cref="Tag"/> is the release tag as published (e.g. "v0.2.0").</summary>
public sealed record UpdateInfo(Version Version, string Tag, Uri DownloadUrl);
