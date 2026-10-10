namespace Island.Core.Idle;

/// <summary>Maps WMO weather codes, as Open-Meteo reports them, to a category and a short pt-BR description.</summary>
public static class WeatherCodeMapper
{
    public sealed record Condition(WeatherCategory Category, string Description);

    /// <summary>Unlisted codes map to <see cref="WeatherCategory.Unknown"/> instead of failing.</summary>
    public static Condition Map(int code) => code switch
    {
        0 => new(WeatherCategory.Clear, "Céu limpo"),
        1 => new(WeatherCategory.Clear, "Quase limpo"),
        2 => new(WeatherCategory.PartlyCloudy, "Parcialmente nublado"),
        3 => new(WeatherCategory.Cloudy, "Nublado"),
        45 or 48 => new(WeatherCategory.Fog, "Neblina"),
        51 or 53 or 55 => new(WeatherCategory.Drizzle, "Garoa"),
        56 or 57 => new(WeatherCategory.Drizzle, "Garoa congelante"),
        61 or 63 or 65 => new(WeatherCategory.Rain, "Chuva"),
        66 or 67 => new(WeatherCategory.Rain, "Chuva congelante"),
        71 or 73 or 75 or 77 => new(WeatherCategory.Snow, "Neve"),
        80 or 81 or 82 => new(WeatherCategory.Rain, "Pancadas de chuva"),
        85 or 86 => new(WeatherCategory.Snow, "Pancadas de neve"),
        95 or 96 or 99 => new(WeatherCategory.Thunderstorm, "Tempestade"),
        _ => new(WeatherCategory.Unknown, "Tempo indefinido"),
    };
}
