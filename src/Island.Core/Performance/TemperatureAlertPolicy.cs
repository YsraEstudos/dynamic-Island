namespace Island.Core.Performance;

public enum AlertSensor
{
    Cpu,
    Gpu,
}

/// <summary>A sensor reached its limit. <see cref="Celsius"/> is the reading that crossed it.</summary>
public sealed record TemperatureAlert(AlertSensor Sensor, double Celsius, int LimitC);

/// <summary>
/// Decides when a temperature alert fires, per sensor. A reading at or above the limit fires while the sensor is
/// armed. Firing disarms it (hysteresis): it re-arms only once the reading has fallen at least
/// <see cref="RearmDropC"/> below the limit. Two alerts of one sensor are at least <see cref="Cooldown"/> apart.
/// A hot reading that arrives during the cooldown waits for it and fires when it ends, if still hot.
/// Pure: the caller passes the time, so nothing here reads the clock or the system.
/// </summary>
public sealed class TemperatureAlertPolicy
{
    public const double RearmDropC = 5.0;
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    private sealed class SensorState
    {
        public bool Armed = true;
        public TimeSpan? LastAlertAt;
    }

    private readonly SensorState _cpu = new();
    private readonly SensorState _gpu = new();

    /// <summary>Alerts raised by this snapshot, possibly none. Disabled settings raise nothing and forget the state.</summary>
    public IReadOnlyList<TemperatureAlert> Evaluate(PerformanceSnapshot snapshot, PerformanceAlertSettings settings, TimeSpan now)
    {
        if (!settings.Enabled)
        {
            Reset();
            return Array.Empty<TemperatureAlert>();
        }

        var alerts = new List<TemperatureAlert>(2);
        Check(AlertSensor.Cpu, _cpu, snapshot.CpuTempC, settings.CpuLimitC, now, alerts);
        Check(AlertSensor.Gpu, _gpu, snapshot.GpuTempC, settings.GpuLimitC, now, alerts);
        return alerts;
    }

    /// <summary>Forgets all state, as on a fresh start: the next hot reading may alert at once.</summary>
    public void Reset()
    {
        foreach (SensorState state in new[] { _cpu, _gpu })
        {
            state.Armed = true;
            state.LastAlertAt = null;
        }
    }

    private static void Check(AlertSensor sensor, SensorState state, double? celsius, int limit, TimeSpan now, List<TemperatureAlert> alerts)
    {
        // No reading keeps the state: a missing sensor must not re-arm or disarm anything.
        if (celsius is not { } temp) return;

        if (temp <= limit - RearmDropC)
        {
            state.Armed = true;
            return;
        }

        if (!state.Armed || temp < limit) return;
        if (state.LastAlertAt is { } last && now - last < Cooldown) return;

        state.Armed = false;
        state.LastAlertAt = now;
        alerts.Add(new TemperatureAlert(sensor, temp, limit));
    }
}
