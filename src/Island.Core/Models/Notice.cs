namespace Island.Core.Models;

/// <summary>Content of a temporary toast. Glyph is an optional icon key understood by the UI (e.g. "check", "timer").</summary>
public sealed record Notice(string Title, string? Subtitle = null, string? Glyph = null);
