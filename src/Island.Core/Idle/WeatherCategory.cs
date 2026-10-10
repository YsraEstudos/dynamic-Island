namespace Island.Core.Idle;

/// <summary>Coarse weather family of a WMO code. Picks the icon; the exact wording comes from <see cref="WeatherCodeMapper"/>.</summary>
public enum WeatherCategory
{
    Unknown,
    Clear,
    PartlyCloudy,
    Cloudy,
    Fog,
    Drizzle,
    Rain,
    Snow,
    Thunderstorm,
}
