using System.Runtime.InteropServices;
using System.Text;
using Island.Core.Clipboard;
using Island.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Clipboard;

/// <summary>
/// Watches the system clipboard and captures user copies of text and images. Event-driven: a message-only window
/// on a dedicated thread receives WM_CLIPBOARDUPDATE. There is no polling.
/// <para>
/// Captured items are raised from the message thread; subscribers marshal to their own thread.
/// Content that password managers mark as private, and anything this process writes itself, is never raised.
/// </para>
/// </summary>
public sealed class WindowsClipboardService : IClipboardService
{
    private const int MaxTextChars = 20_000;
    private const int MaxImageBytes = 25 * 1024 * 1024;
    private const int DebounceMs = 300;
    private const int OpenAttempts = 5;
    private const int OpenRetryDelayMs = 20;
    private const int WriteAttempts = 3;
    private const int ReadAttempts = 3;
    private const int ErrorClipboardNotOpen = 1418;

    private readonly ILogger<WindowsClipboardService>? _logger;

    // Registered formats used for the privacy checks and for PNG output. 0 means the registration failed.
    private readonly uint _formatExcludeFromMonitoring;
    private readonly uint _formatCanIncludeInHistory;
    private readonly uint _formatCanUploadToCloud;
    private readonly uint _formatPng;

    private readonly object _lifecycle = new();
    private readonly object _state = new();   // guards _ownSequence, _lastText, _lastTextTick

    private volatile MessageWindowThread? _window;
    private volatile bool _disposed;
    private bool _started;

    // Our own writes: suppress while in progress, then ignore the update whose sequence number matches the write.
    private int _suppressDepth;
    private uint? _ownSequence;

    private string? _lastText;
    private long _lastTextTick;

    public WindowsClipboardService(ILogger<WindowsClipboardService>? logger = null)
    {
        _logger = logger;
        _formatExcludeFromMonitoring = RegisterFormat("ExcludeClipboardContentFromMonitorProcessing");
        _formatCanIncludeInHistory = RegisterFormat("CanIncludeInClipboardHistory");
        _formatCanUploadToCloud = RegisterFormat("CanUploadToCloudClipboard");
        _formatPng = RegisterFormat("PNG");
    }

    public event EventHandler<ClipboardItem>? ItemCaptured;

    /// <summary>Creates the listener window and starts watching the clipboard. Idempotent.</summary>
    public void Start()
    {
        lock (_lifecycle)
        {
            if (_started || _disposed) return;
            _started = true;

            var window = new MessageWindowThread("Clipboard", OnWindowMessage, _logger);
            if (!window.Start())
            {
                _logger?.LogWarning("Clipboard listener window could not be created; clipboard capture is disabled.");
                window.Dispose();
                return;
            }

            _window = window;
            IntPtr hwnd = window.Handle;
            if (!window.Invoke(() => NativeMethods.AddClipboardFormatListener(hwnd)))
                _logger?.LogWarning("AddClipboardFormatListener failed; clipboard capture is disabled.");
        }
    }

    /// <summary>Puts text on the clipboard. Never raises ItemCaptured for this write.</summary>
    public void SetText(string text)
    {
        try
        {
            if (string.IsNullOrEmpty(text)) return;
            byte[] utf16 = Encoding.Unicode.GetBytes(text + '\0');   // CF_UNICODETEXT is null-terminated
            if (!WriteClipboard((NativeMethods.CF_UNICODETEXT, utf16)))
                _logger?.LogWarning("SetText: the clipboard could not be written.");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SetText failed.");
        }
    }

    /// <summary>
    /// Puts an image on the clipboard. BMP goes out as CF_DIB (file header stripped); PNG goes out under the "PNG"
    /// format. Other formats are rejected because nothing here decodes them.
    /// </summary>
    public void SetImage(byte[] imageBytes)
    {
        try
        {
            if (imageBytes is null || imageBytes.Length == 0 || imageBytes.Length > MaxImageBytes) return;

            if (ClipboardImageConverter.IsBmp(imageBytes))
            {
                if (!WriteClipboard((NativeMethods.CF_DIB, ClipboardImageConverter.BmpToDib(imageBytes))))
                    _logger?.LogWarning("SetImage: the clipboard could not be written.");
            }
            else if (ClipboardImageConverter.IsPng(imageBytes))
            {
                if (_formatPng == 0)
                {
                    _logger?.LogWarning("SetImage: the 'PNG' clipboard format could not be registered.");
                    return;
                }
                if (!WriteClipboard((_formatPng, imageBytes)))
                    _logger?.LogWarning("SetImage: the clipboard could not be written.");
            }
            else
            {
                _logger?.LogWarning("SetImage: only BMP and PNG bytes are supported.");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SetImage failed.");
        }
    }

    public void Dispose()
    {
        MessageWindowThread? window;
        lock (_lifecycle)
        {
            if (_disposed) return;
            _disposed = true;
            window = _window;
            _window = null;
        }
        if (window is null) return;

        IntPtr hwnd = window.Handle;
        window.Invoke(() => NativeMethods.RemoveClipboardFormatListener(hwnd));
        window.Dispose();
    }

    private static uint RegisterFormat(string name)
    {
        try { return NativeMethods.RegisterClipboardFormatW(name); }
        catch (Exception) { return 0; }
    }

    private IntPtr? OnWindowMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != NativeMethods.WM_CLIPBOARDUPDATE) return null;
        HandleClipboardUpdate();
        return IntPtr.Zero;
    }

    private void HandleClipboardUpdate()
    {
        if (_disposed || Volatile.Read(ref _suppressDepth) > 0) return;

        ClipboardItem? item = null;
        for (int attempt = 1; attempt <= ReadAttempts; attempt++)
        {
            if (!TryOpenClipboard(IntPtr.Zero))
            {
                _logger?.LogDebug("Clipboard is busy; skipping this update.");
                return;
            }

            bool lockLost = false;
            try
            {
                item = ReadCapturableItem();
            }
            catch (ClipboardLockLostException)
            {
                lockLost = true;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Reading the clipboard failed.");
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }

            if (!lockLost) break;
            _logger?.LogDebug("The clipboard lock was lost while reading (attempt {Attempt}).", attempt);
            if (attempt < ReadAttempts) Thread.Sleep(OpenRetryDelayMs);
        }

        // Raised with the clipboard closed, so subscribers never block other apps' clipboard access.
        if (item is not null) RaiseItemCaptured(item);
    }

    /// <summary>Runs with the clipboard open. Returns null for anything that should not be captured.</summary>
    private ClipboardItem? ReadCapturableItem()
    {
        uint sequence = NativeMethods.GetClipboardSequenceNumber();
        lock (_state)
        {
            if (_ownSequence == sequence) return null;
        }

        if (IsOwnedByThisProcess()) return null;
        if (IsMarkedPrivate()) return null;

        if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT))
        {
            string? text = ReadUnicodeText()?.Trim();
            if (string.IsNullOrEmpty(text)) return null;

            long now = Environment.TickCount64;
            lock (_state)
            {
                if (text == _lastText && now - _lastTextTick < DebounceMs) return null;
                _lastText = text;
                _lastTextTick = now;
            }
            return ClipboardItem.FromText(text);
        }

        byte[]? bmp = ReadImageAsBmp();
        return bmp is null ? null : ClipboardItem.FromImage(bmp);
    }

    /// <summary>
    /// The clipboard owner is a window of this process. That covers our own writes made with our window as owner,
    /// and also copies made inside this app, which are ignored by design.
    /// </summary>
    private static bool IsOwnedByThisProcess()
    {
        IntPtr owner = NativeMethods.GetClipboardOwner();
        if (owner == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(owner, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    private bool IsMarkedPrivate()
    {
        if (_formatExcludeFromMonitoring != 0 && NativeMethods.IsClipboardFormatAvailable(_formatExcludeFromMonitoring))
            return true;
        if (IsDwordFormatZero(_formatCanIncludeInHistory)) return true;
        if (IsDwordFormatZero(_formatCanUploadToCloud)) return true;
        return false;
    }

    private static bool IsDwordFormatZero(uint format)
    {
        if (format == 0 || !NativeMethods.IsClipboardFormatAvailable(format)) return false;

        IntPtr handle = GetClipboardDataChecked(format);
        if (handle == IntPtr.Zero) return false;

        ulong size = NativeMethods.GlobalSize(handle);
        if (size < sizeof(int)) return false;

        IntPtr ptr = NativeMethods.GlobalLock(handle);
        if (ptr == IntPtr.Zero) return false;
        try
        {
            return Marshal.ReadInt32(ptr) == 0;
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    private static unsafe string? ReadUnicodeText()
    {
        IntPtr handle = GetClipboardDataChecked(NativeMethods.CF_UNICODETEXT);
        if (handle == IntPtr.Zero) return null;

        ulong sizeBytes = NativeMethods.GlobalSize(handle);
        IntPtr ptr = NativeMethods.GlobalLock(handle);
        if (ptr == IntPtr.Zero) return null;
        try
        {
            // Bounded by the allocation size as well as the cap, in case the terminator is missing.
            int maxChars = (int)Math.Min(sizeBytes / 2, (ulong)MaxTextChars);
            char* chars = (char*)ptr;
            int length = 0;
            while (length < maxChars && chars[length] != '\0') length++;
            return new string(chars, 0, length);
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    private static byte[]? ReadImageAsBmp()
    {
        uint format = NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_DIBV5) ? NativeMethods.CF_DIBV5
            : NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_DIB) ? NativeMethods.CF_DIB
            : 0;
        if (format == 0) return null;   // CF_BITMAP only (a GDI handle) is not supported

        IntPtr handle = GetClipboardDataChecked(format);
        if (handle == IntPtr.Zero) return null;

        ulong size = NativeMethods.GlobalSize(handle);
        if (size < 40 || size > MaxImageBytes) return null;

        IntPtr ptr = NativeMethods.GlobalLock(handle);
        if (ptr == IntPtr.Zero) return null;

        byte[] dib;
        try
        {
            dib = new byte[(int)size];
            Marshal.Copy(ptr, dib, 0, dib.Length);
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
        return ClipboardImageConverter.DibToBmp(dib);
    }

    /// <summary>
    /// GetClipboardData that reports a lost clipboard lock. WM_CLIPBOARDUPDATE can arrive while the writer's own
    /// CloseClipboard is still finishing; our OpenClipboard then succeeds, but the writer's tail end drops the lock,
    /// so every read fails with ERROR_CLIPBOARD_NOT_OPEN even though IsClipboardFormatAvailable still says yes.
    /// Closing and reopening gets a clean lock.
    /// </summary>
    private static IntPtr GetClipboardDataChecked(uint format)
    {
        IntPtr handle = NativeMethods.GetClipboardData(format);
        if (handle == IntPtr.Zero && Marshal.GetLastPInvokeError() == ErrorClipboardNotOpen)
            throw new ClipboardLockLostException();
        return handle;
    }

    private sealed class ClipboardLockLostException : Exception;

    /// <summary>
    /// Opens the clipboard with retries (another app may hold it briefly). Returns false if it stays busy.
    /// </summary>
    private static bool TryOpenClipboard(IntPtr owner)
    {
        for (int attempt = 1; attempt <= OpenAttempts; attempt++)
        {
            if (NativeMethods.OpenClipboard(owner)) return true;
            if (attempt < OpenAttempts) Thread.Sleep(OpenRetryDelayMs);
        }
        return false;
    }

    /// <summary>
    /// Replaces the clipboard content with the given formats and records the resulting sequence number, so the
    /// update that our own write triggers is ignored. Safe to call from any thread.
    /// </summary>
    private bool WriteClipboard(params (uint Format, byte[] Data)[] formats)
    {
        Interlocked.Increment(ref _suppressDepth);
        try
        {
            for (int attempt = 1; attempt <= WriteAttempts; attempt++)
            {
                if (TryWriteOnce(formats)) return true;
                if (attempt < WriteAttempts) Thread.Sleep(OpenRetryDelayMs);
            }
            return false;
        }
        finally
        {
            Interlocked.Decrement(ref _suppressDepth);
        }
    }

    private bool TryWriteOnce((uint Format, byte[] Data)[] formats)
    {
        // Our window becomes the clipboard owner, which the owner check then recognises as ours.
        IntPtr owner = _window?.Handle ?? IntPtr.Zero;
        if (!TryOpenClipboard(owner)) return false;

        try
        {
            if (!NativeMethods.EmptyClipboard()) return false;

            foreach (var (format, data) in formats)
            {
                IntPtr memory = AllocGlobal(data);
                if (memory == IntPtr.Zero) return false;

                // On success the system owns the memory. On failure it is ours to free.
                if (NativeMethods.SetClipboardData(format, memory) == IntPtr.Zero)
                {
                    NativeMethods.GlobalFree(memory);
                    return false;
                }
            }

            // Read while the clipboard is still open, so no other writer can slip in between.
            uint sequence = NativeMethods.GetClipboardSequenceNumber();
            lock (_state)
            {
                _ownSequence = sequence;
            }
            return true;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static IntPtr AllocGlobal(byte[] data)
    {
        IntPtr memory = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (nuint)data.Length);
        if (memory == IntPtr.Zero) return IntPtr.Zero;

        IntPtr ptr = NativeMethods.GlobalLock(memory);
        if (ptr == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(memory);
            return IntPtr.Zero;
        }

        Marshal.Copy(data, 0, ptr, data.Length);
        NativeMethods.GlobalUnlock(memory);
        return memory;
    }

    private void RaiseItemCaptured(ClipboardItem item)
    {
        try
        {
            ItemCaptured?.Invoke(this, item);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ItemCaptured handler threw.");
        }
    }
}
