using System.Globalization;
using Island.Core.Tools.FocusSounds;

namespace Island.App.Views.Widgets;

/// <summary>Text of the Sons de foco widget, kept apart from the widget so the wording can be read and tested on its own.</summary>
public static class FocusSoundsFormat
{
    public const string Title = "Sons de foco";
    public const string NoAudioOutput = "Sem saída de áudio";
    public const string PickASound = "Escolha um som";
    public const string NoTimer = "Sem timer";

    /// <summary>Short name shown on a chip.</summary>
    public static string SoundName(FocusSoundKind kind) => kind switch
    {
        FocusSoundKind.Rain => "Chuva",
        FocusSoundKind.WhiteNoise => "Branco",
        FocusSoundKind.PinkNoise => "Rosa",
        FocusSoundKind.BrownNoise => "Marrom",
        FocusSoundKind.Waves => "Ondas",
        FocusSoundKind.Fire => "Fogueira",
        FocusSoundKind.Wind => "Vento",
        FocusSoundKind.Cafe => "Café",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown focus sound."),
    };

    /// <summary>Level as a whole percentage, for example 60%.</summary>
    public static string Percent(float level)
    {
        int percent = (int)MathF.Round(Math.Clamp(level, 0f, 1f) * 100f);
        return percent.ToString(CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>Sleep button label: the chosen duration while idle, the time left while it runs.</summary>
    public static string SleepLabel(int minutes, TimeSpan? remaining)
    {
        if (minutes == 0) return NoTimer;
        if (remaining is not { } left) return $"{minutes} min";

        // Rounding up keeps the label from showing 0 min while time is still left.
        if (left.TotalMinutes >= 1) return $"{(int)Math.Ceiling(left.TotalMinutes)} min";
        return $"{(int)Math.Ceiling(left.TotalSeconds)} s";
    }
}
