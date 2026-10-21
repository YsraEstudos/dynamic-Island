using System.Runtime.InteropServices;
using Island.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Island.Windows.VirtualDesktops;

/// <summary>
/// Windows virtual desktops through the shell's undocumented COM service (the one behind Win+Tab). Verified on Windows 11
/// build 26300 only. A missing or changed interface, or a failed call, makes <see cref="IsAvailable"/> false and the
/// feature turns off; nothing is thrown to the caller. Creating the service is retried after 30 s, not on every call.
/// </summary>
/// <remarks>
/// All calls run under one lock, so the COM objects are never used from two threads at once.
/// </remarks>
public sealed class WindowsVirtualDesktopService : IVirtualDesktopService
{
    private static readonly Guid ImmersiveShellClsid = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid VirtualDesktopManagerInternalService = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
    private static readonly Guid VirtualDesktopIid = new("3F07F4BE-B107-441A-AF0F-39D82529072C");
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);

    private readonly ILogger<WindowsVirtualDesktopService>? _logger;
    private readonly object _gate = new();
    private IVirtualDesktopManagerInternal? _manager;
    private DateTime _retryAtUtc = DateTime.MinValue;

    public WindowsVirtualDesktopService(ILogger<WindowsVirtualDesktopService>? logger = null) => _logger = logger;

    public bool IsAvailable
    {
        get
        {
            lock (_gate) return Manager() is not null;
        }
    }

    public IReadOnlyList<VirtualDesktopInfo> GetDesktops()
    {
        lock (_gate)
        {
            var manager = Manager();
            if (manager is null) return Array.Empty<VirtualDesktopInfo>();

            try
            {
                var desktops = Enumerate(manager);
                var result = new VirtualDesktopInfo[desktops.Count];
                for (int i = 0; i < desktops.Count; i++)
                    result[i] = new VirtualDesktopInfo(desktops[i].GetId(), NameOf(desktops[i], i), i);
                return result;
            }
            catch (Exception ex)
            {
                Drop(ex);
                return Array.Empty<VirtualDesktopInfo>();
            }
        }
    }

    public Guid? GetCurrentDesktopId()
    {
        lock (_gate)
        {
            var manager = Manager();
            if (manager is null) return null;

            try
            {
                return manager.GetCurrentDesktop().GetId();
            }
            catch (Exception ex)
            {
                Drop(ex);
                return null;
            }
        }
    }

    public bool SwitchTo(Guid desktopId)
    {
        lock (_gate)
        {
            var manager = Manager();
            if (manager is null) return false;

            try
            {
                var target = Enumerate(manager).FirstOrDefault(d => d.GetId() == desktopId);
                if (target is null) return false;

                manager.SwitchDesktop(target);
                return true;
            }
            catch (Exception ex)
            {
                Drop(ex);
                return false;
            }
        }
    }

    // Caller holds the gate. Null while the service cannot be created; retried after RetryAfterFailure.
    private IVirtualDesktopManagerInternal? Manager()
    {
        if (_manager is not null) return _manager;
        if (DateTime.UtcNow < _retryAtUtc) return null;

        try
        {
            var shellType = Type.GetTypeFromCLSID(ImmersiveShellClsid, throwOnError: true)!;
            var shell = (IServiceProvider10)Activator.CreateInstance(shellType)!;
            var service = VirtualDesktopManagerInternalService;
            var iid = typeof(IVirtualDesktopManagerInternal).GUID;
            _manager = (IVirtualDesktopManagerInternal)shell.QueryService(ref service, ref iid);
            return _manager;
        }
        catch (Exception ex)
        {
            _retryAtUtc = DateTime.UtcNow + RetryAfterFailure;
            _logger?.LogWarning(ex, "Virtual desktops are unavailable; the Angry desktop lock is off.");
            return null;
        }
    }

    // Caller holds the gate. A failed call usually means the shell restarted: drop the service so it is created again.
    private void Drop(Exception ex)
    {
        _manager = null;
        _logger?.LogDebug(ex, "Virtual desktop call failed; it will be retried on the next use.");
    }

    private static List<IVirtualDesktop> Enumerate(IVirtualDesktopManagerInternal manager)
    {
        manager.GetDesktops(out var array);
        array.GetCount(out int count);

        var list = new List<IVirtualDesktop>(count);
        for (int i = 0; i < count; i++)
        {
            var iid = VirtualDesktopIid;
            array.GetAt(i, ref iid, out object item);
            list.Add((IVirtualDesktop)item);
        }

        return list;
    }

    // Desktops nobody named report an empty name; the list shows them by position instead.
    private static string NameOf(IVirtualDesktop desktop, int index)
    {
        string name = string.Empty;
        IntPtr hstring = desktop.GetName();
        if (hstring != IntPtr.Zero)
        {
            try
            {
                IntPtr raw = WindowsGetStringRawBuffer(hstring, out uint length);
                name = Marshal.PtrToStringUni(raw, (int)length) ?? string.Empty;
            }
            finally
            {
                WindowsDeleteString(hstring);
            }
        }

        return string.IsNullOrWhiteSpace(name) ? $"Área de trabalho {index + 1}" : name.Trim();
    }

    [DllImport("combase.dll")]
    private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);
}
