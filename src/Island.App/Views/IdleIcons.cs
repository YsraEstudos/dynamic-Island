using System.Windows.Media;
using Island.Core.Idle;

namespace Island.App.Views;

/// <summary>
/// Stroke icons of the idle capsule, on a 24-unit grid (the same convention as the UI icons in Controls.xaml).
/// They are static geometry: the capsule never animates them.
/// </summary>
public static class IdleIcons
{
    public static Geometry Sun { get; } = Frozen("M12,8.5 A3.5,3.5 0 1 0 12,15.5 A3.5,3.5 0 1 0 12,8.5 Z M12,2.5 V4.5 M12,19.5 V21.5 M2.5,12 H4.5 M19.5,12 H21.5 M5.3,5.3 L6.7,6.7 M17.3,17.3 L18.7,18.7 M5.3,18.7 L6.7,17.3 M17.3,6.7 L18.7,5.3");

    public static Geometry Cloud { get; } = Frozen("M7,18 H17 A4,4 0 0 0 17,10 A5.5,5.5 0 0 0 6.5,11.5 A3.2,3.2 0 0 0 7,18 Z");

    public static Geometry Fog { get; } = Frozen("M5,9 H19 M7,13 H17 M5,17 H19");

    public static Geometry Rain { get; } = Frozen("M7,15 H17 A4,4 0 0 0 17,7 A5.5,5.5 0 0 0 6.5,8.5 A3.2,3.2 0 0 0 7,15 Z M8,19 L7,21.5 M12,19 L11,21.5 M16,19 L15,21.5");

    public static Geometry Snow { get; } = Frozen("M7,15 H17 A4,4 0 0 0 17,7 A5.5,5.5 0 0 0 6.5,8.5 A3.2,3.2 0 0 0 7,15 Z M8,19.5 H8.01 M12,21 H12.01 M16,19.5 H16.01");

    public static Geometry Storm { get; } = Frozen("M7,15 H17 A4,4 0 0 0 17,7 A5.5,5.5 0 0 0 6.5,8.5 A3.2,3.2 0 0 0 7,15 Z M12.5,16 L10,20 H13.5 L11,23.5");

    /// <summary>Scheduled pomodoro start: the clock of the existing timer icon.</summary>
    public static Geometry Timer { get; } = Frozen("M12,6.5 A6.5,6.5 0 1 0 12,19.5 A6.5,6.5 0 1 0 12,6.5 Z M12,9.5 V13 L14.5,14.8 M9.5,3.5 H14.5");

    /// <summary>Chance of rain next to the weather reading.</summary>
    public static Geometry RainDrop { get; } = Frozen("M12,3 C12,3 6.5,9.5 6.5,13.5 A5.5,5.5 0 0 0 17.5,13.5 C17.5,9.5 12,3 12,3 Z");

    /// <summary>The icon of a weather family. Cloudy and unknown skies share the cloud.</summary>
    public static Geometry For(WeatherCategory category) => category switch
    {
        WeatherCategory.Clear => Sun,
        WeatherCategory.Fog => Fog,
        WeatherCategory.Drizzle or WeatherCategory.Rain => Rain,
        WeatherCategory.Snow => Snow,
        WeatherCategory.Thunderstorm => Storm,
        _ => Cloud,
    };

    private static Geometry Frozen(string data)
    {
        Geometry geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
