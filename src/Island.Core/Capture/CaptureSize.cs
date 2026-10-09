namespace Island.Core.Capture;

/// <summary>Chooses the recorded frame size. H.264 needs even dimensions, so both sides are rounded down to even.</summary>
public static class CaptureSize
{
    public const int DefaultMaxWidth = 1920;
    public const int DefaultMaxHeight = 1080;

    /// <summary>
    /// Scales <paramref name="width"/> x <paramref name="height"/> down (never up) to fit the limits, keeping the aspect ratio.
    /// </summary>
    public static (int Width, int Height) Fit(int width, int height, int maxWidth = DefaultMaxWidth, int maxHeight = DefaultMaxHeight)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "The source size must be positive.");

        double scale = Math.Min(1.0, Math.Min((double)maxWidth / width, (double)maxHeight / height));
        return (Even(width * scale), Even(height * scale));
    }

    /// <summary>A bitrate that looks good for games without wasting space: about 1 bit per 8 pixels per frame.</summary>
    public static int BitrateFor(int width, int height, int fps) =>
        (int)Math.Clamp((long)width * height * fps / 8, 2_000_000L, 12_000_000L);

    private static int Even(double value)
    {
        int rounded = (int)value & ~1;
        return Math.Max(rounded, 2);
    }
}
