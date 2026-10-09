using System.Text.RegularExpressions;

namespace Island.Core.Performance;

/// <summary>
/// Turns the raw "GPU Engine" and "GPU Adapter Memory" counter instances into adapter figures. Pure, so the parsing
/// and the rules can be tested without the counters. Instance names look like
/// <c>pid_1234_luid_0x00000000_0x0000E4D2_phys_0_eng_3_engtype_3D</c>.
/// </summary>
public static partial class GpuEngineUsage
{
    /// <summary>
    /// Utilisation of the busiest adapter. For each adapter and engine type the process values are summed and capped
    /// at 100; the adapter takes its busiest engine type, and the result is the busiest adapter. Engines run in
    /// parallel, so the busiest one is the figure that matches what a person sees as "GPU load".
    /// Assumption: this matches Task Manager closely but was not verified against it on real hardware.
    /// </summary>
    public static double? Busiest(IEnumerable<(string Instance, double Value)> samples)
    {
        var perEngine = new Dictionary<(string Adapter, string Engine), double>();
        foreach ((string instance, double value) in samples)
        {
            if (!double.IsFinite(value)) continue;
            Match match = EngineInstance().Match(instance);
            if (!match.Success) continue;

            var key = (match.Groups["phys"].Value, match.Groups["engine"].Value);
            perEngine[key] = perEngine.GetValueOrDefault(key) + value;
        }

        if (perEngine.Count == 0) return null;

        var perAdapter = new Dictionary<string, double>();
        foreach (((string adapter, _), double total) in perEngine)
        {
            perAdapter[adapter] = Math.Max(perAdapter.GetValueOrDefault(adapter), Math.Min(total, 100.0));
        }
        return perAdapter.Values.Max();
    }

    /// <summary>Dedicated video memory in use on the adapter with the most of it, in bytes.</summary>
    public static ulong? DedicatedBytes(IEnumerable<(string Instance, double Value)> samples)
    {
        var perAdapter = new Dictionary<string, double>();
        foreach ((string instance, double value) in samples)
        {
            if (!double.IsFinite(value) || value < 0) continue;
            Match match = AdapterInstance().Match(instance);
            if (!match.Success) continue;

            string adapter = match.Groups["phys"].Value;
            perAdapter[adapter] = perAdapter.GetValueOrDefault(adapter) + value;
        }

        if (perAdapter.Count == 0) return null;
        return (ulong)perAdapter.Values.Max();
    }

    [GeneratedRegex(@"phys_(?<phys>\d+)_eng_\d+_engtype_(?<engine>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex EngineInstance();

    [GeneratedRegex(@"phys_(?<phys>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex AdapterInstance();
}
