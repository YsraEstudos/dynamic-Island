using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Island.Windows.Performance;

/// <summary>
/// One PDH query on a wildcard counter path such as <c>\GPU Engine(*)\Utilization Percentage</c>. A single collect
/// reads every instance at once. That is far cheaper than one PerformanceCounter object (and handle) per instance,
/// which the GPU engine category would need hundreds of. English counter names keep it working in any display language.
/// The item layout below is for 64-bit processes; the app ships only win-x64.
/// Not thread-safe: the sampler calls it from one thread at a time.
/// </summary>
internal sealed class PdhWildcardCounter : IDisposable
{
    // PDH_CSTATUS_VALID_DATA and PDH_CSTATUS_NEW_DATA: the only item statuses carrying a usable value.
    private const uint CstatusValidData = 0x00000000;
    private const uint CstatusNewData = 0x00000001;
    // PDH_FMT_COUNTERVALUE_ITEM_W on x64: name pointer (8), CStatus (4) + padding (4), double value (8).
    private const int ItemSize = 24;
    private const int ItemStatusOffset = 8;
    private const int ItemValueOffset = 16;

    private IntPtr _query;
    private IntPtr _counter;

    /// <exception cref="Win32Exception">The counter path is not known to this machine.</exception>
    public PdhWildcardCounter(string path)
    {
        if (IntPtr.Size != 8) throw new PlatformNotSupportedException("The PDH item layout is written for 64-bit processes.");

        CheckStatus(PerformanceNativeMethods.PdhOpenQuery(null, IntPtr.Zero, out _query), "PdhOpenQuery");
        try
        {
            CheckStatus(PerformanceNativeMethods.PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out _counter), path);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Collects once and returns every instance with a valid value. The first collect of a rate counter has no
    /// previous sample, so it returns an empty list. Any PDH failure also returns an empty list.
    /// </summary>
    public List<(string Instance, double Value)> Collect()
    {
        var result = new List<(string Instance, double Value)>();
        if (_query == IntPtr.Zero || _counter == IntPtr.Zero) return result;
        if (PerformanceNativeMethods.PdhCollectQueryData(_query) != PerformanceNativeMethods.PdhSuccess) return result;

        uint size = 0;
        uint count = 0;
        uint status = PerformanceNativeMethods.PdhGetFormattedCounterArrayW(
            _counter, PerformanceNativeMethods.PdhFmtDouble, ref size, ref count, IntPtr.Zero);
        if (status != PerformanceNativeMethods.PdhMoreData || size == 0) return result;

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            status = PerformanceNativeMethods.PdhGetFormattedCounterArrayW(
                _counter, PerformanceNativeMethods.PdhFmtDouble, ref size, ref count, buffer);
            if (status != PerformanceNativeMethods.PdhSuccess) return result;

            for (int i = 0; i < count; i++)
            {
                IntPtr item = buffer + i * ItemSize;
                uint itemStatus = unchecked((uint)Marshal.ReadInt32(item, ItemStatusOffset));
                if (itemStatus is not (CstatusValidData or CstatusNewData)) continue;

                IntPtr name = Marshal.ReadIntPtr(item);
                double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, ItemValueOffset));
                if (name != IntPtr.Zero && double.IsFinite(value))
                    result.Add((Marshal.PtrToStringUni(name) ?? string.Empty, value));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return result;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            PerformanceNativeMethods.PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
        _counter = IntPtr.Zero;
    }

    private static void CheckStatus(uint status, string operation)
    {
        if (status != PerformanceNativeMethods.PdhSuccess)
            throw new Win32Exception((int)status, $"{operation} failed (PDH status 0x{status:X8}).");
    }
}
