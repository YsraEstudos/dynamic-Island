namespace Island.Core.Idle;

/// <summary>
/// Where the forecast is read for. <paramref name="Query"/> is the city the user typed, or empty when the place is
/// the approximate location of this PC (found by IP). The query is how a cached place is matched to the setting.
/// </summary>
public sealed record WeatherPlace(string Query, double Latitude, double Longitude);
