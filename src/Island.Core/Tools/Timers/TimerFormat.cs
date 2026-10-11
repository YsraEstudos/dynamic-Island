namespace Island.Core.Tools.Timers;

/// <summary>Text the timer widget shows. Pure, so the rounding rules are tested without a window.</summary>
public static class TimerFormat
{
    private const long TicksPerCentisecond = TimeSpan.TicksPerMillisecond * 10;

    /// <summary>Remaining time as "mm:ss", or "h:mm:ss" from one hour. Rounds up, so it reads 00:00 only once the time is up.</summary>
    public static string Countdown(TimeSpan remaining)
    {
        long ticks = Math.Max(0, remaining.Ticks);
        long seconds = (ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond;
        return Clock(seconds);
    }

    /// <summary>Elapsed time as "mm:ss,cc", or "h:mm:ss,cc" from one hour. Hundredths are truncated, never ahead of the clock.</summary>
    public static string Stopwatch(TimeSpan elapsed)
    {
        long centiseconds = Math.Max(0, elapsed.Ticks) / TicksPerCentisecond;
        int hundredths = (int)(centiseconds % 100);
        return FormattableString.Invariant($"{Clock(centiseconds / 100)},{hundredths:00}");
    }

    /// <summary>Duration label for a preset: "25 min", "1 min 10 s" or "10 s".</summary>
    public static string Duration(TimeSpan duration)
    {
        long totalSeconds = Math.Max(0, duration.Ticks) / TimeSpan.TicksPerSecond;
        long minutes = totalSeconds / 60;
        long seconds = totalSeconds % 60;

        if (minutes == 0) return FormattableString.Invariant($"{seconds} s");
        if (seconds == 0) return FormattableString.Invariant($"{minutes} min");
        return FormattableString.Invariant($"{minutes} min {seconds} s");
    }

    private static string Clock(long totalSeconds)
    {
        long hours = totalSeconds / 3600;
        long minutes = totalSeconds / 60 % 60;
        long seconds = totalSeconds % 60;

        return hours > 0
            ? FormattableString.Invariant($"{hours}:{minutes:00}:{seconds:00}")
            : FormattableString.Invariant($"{minutes:00}:{seconds:00}");
    }
}
