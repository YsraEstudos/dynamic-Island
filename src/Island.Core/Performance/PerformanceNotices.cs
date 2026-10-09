using System.Globalization;
using Island.Core.Models;

namespace Island.Core.Performance;

/// <summary>
/// Toast texts for temperature alerts. Deliberately not urgent: like the Caps Lock and device notices, they respect
/// pause and fullscreen and never break through the shelf or the Mini pill.
/// </summary>
public static class PerformanceNotices
{
    public static Notice For(TemperatureAlert alert)
    {
        string sensor = alert.Sensor == AlertSensor.Cpu ? "CPU" : "GPU";
        string temperature = alert.Celsius.ToString("0", CultureInfo.InvariantCulture);
        return new Notice(
            $"{sensor} {temperature} °C",
            $"Acima do limite de {alert.LimitC} °C");
    }
}
