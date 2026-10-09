using System.ComponentModel;
using Island.Core.Performance;

namespace Island.Windows.Performance;

/// <summary>
/// CPU temperature from the ACPI "Thermal Zone Information" counters, read without elevation. Only zones whose instance
/// name mentions the CPU are used: a battery or chassis zone must not raise a CPU alert. Many PCs expose no such zone, and
/// then the reading is null. Implausible values are rejected by <see cref="PerformanceValues.Temperature"/>.
/// Called from one thread at a time.
/// </summary>
internal sealed class ThermalZoneReader : IDisposable
{
    private const string PreciseCounterPath = @"\Thermal Zone Information(*)\High Precision Temperature";
    /// <summary>Kelvin, the fallback when the precise counter is missing.</summary>
    private const string PlainCounterPath = @"\Thermal Zone Information(*)\Temperature";
    private const double KelvinOffset = 273.15;

    /// <summary>After the zones cannot be opened, the next attempt waits this long.</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(60);

    private PdhWildcardCounter? _zones;
    private bool _precise;
    private DateTime _nextOpenUtc = DateTime.MinValue;
    private bool _disposed;

    public double? Read()
    {
        if (_disposed) return null;

        try
        {
            if (_zones is null)
            {
                if (DateTime.UtcNow < _nextOpenUtc) return null;
                Open();
                if (_zones is null) return null;
            }

            double? hottest = null;
            foreach ((string instance, double raw) in _zones.Collect())
            {
                if (!instance.Contains("CPU", StringComparison.OrdinalIgnoreCase)) continue;

                double? celsius = _precise
                    ? PerformanceValues.Temperature(raw / 10.0 - KelvinOffset)
                    : PerformanceValues.Temperature(raw - KelvinOffset);
                if (celsius is { } c && (hottest is not { } h || c > h)) hottest = c;
            }
            return hottest;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or PlatformNotSupportedException)
        {
            CloseZones();
            _nextOpenUtc = DateTime.UtcNow + RetryInterval;
            return null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        CloseZones();
    }

    private void Open()
    {
        _nextOpenUtc = DateTime.UtcNow + RetryInterval;
        try
        {
            _zones = new PdhWildcardCounter(PreciseCounterPath);
            _precise = true;
        }
        catch (Win32Exception)
        {
            _zones = new PdhWildcardCounter(PlainCounterPath);
            _precise = false;
        }
    }

    private void CloseZones()
    {
        _zones?.Dispose();
        _zones = null;
    }
}
