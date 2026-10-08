namespace Island.Core.Models;

/// <summary>Master volume. Level is 0..100.</summary>
public sealed record VolumeInfo(int Level, bool IsMuted);
