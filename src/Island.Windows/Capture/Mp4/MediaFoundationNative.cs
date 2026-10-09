using System.Runtime.InteropServices;

namespace Island.Windows.Capture.Mp4;

/// <summary>
/// Media Foundation entry points and the GUIDs the MP4 writer needs. Interface methods are called through the vtable
/// (see <see cref="MediaFoundationCalls"/>), so no interface IIDs are needed and no QueryInterface happens.
/// </summary>
internal static partial class MediaFoundationNative
{
    /// <summary>MF_VERSION: SDK 2 and API 0x70.</summary>
    public const uint MfVersion = 0x00020070;

    public const uint MfStartupFull = 0;

    public const uint MfPackedInterlaceProgressive = 2; // MFVideoInterlace_Progressive

    public static readonly Guid MediaTypeVideo = new("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid VideoFormatH264 = new("34363248-0000-0010-8000-00AA00389B71");
    public static readonly Guid VideoFormatRgb32 = new("00000016-0000-0010-8000-00AA00389B71");

    public static readonly Guid MfMtMajorType = new("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
    public static readonly Guid MfMtSubtype = new("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
    public static readonly Guid MfMtAvgBitrate = new("20332624-FB0D-4D9E-BD0D-CBF6786C102E");
    public static readonly Guid MfMtInterlaceMode = new("E2724BB8-E676-4806-B4B2-A8D6EFB44CCD");
    public static readonly Guid MfMtFrameSize = new("1652C33D-D6B2-4012-B834-72030849A37D");
    public static readonly Guid MfMtFrameRate = new("C459A2E8-3D2C-4E44-B132-FEE5156C7BB0");
    public static readonly Guid MfMtPixelAspectRatio = new("C6376A1E-8D0A-4027-BE45-6D9A0AD39BB6");

    [LibraryImport("mfplat.dll")]
    public static partial int MFStartup(uint version, uint flags);

    [LibraryImport("mfplat.dll")]
    public static partial int MFShutdown();

    [LibraryImport("mfplat.dll")]
    public static partial int MFCreateMediaType(out IntPtr mediaType);

    [LibraryImport("mfplat.dll")]
    public static partial int MFCreateMemoryBuffer(uint maxLength, out IntPtr buffer);

    [LibraryImport("mfplat.dll")]
    public static partial int MFCreateSample(out IntPtr sample);

    /// <summary>The container comes from the file extension (.mp4). A null byte stream and attributes use the defaults.</summary>
    [LibraryImport("mfreadwrite.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IntPtr attributes, out IntPtr sinkWriter);

    /// <summary>Packs two 32-bit values into a UINT64 attribute (MFSetAttributeSize / MFSetAttributeRatio layout).</summary>
    public static ulong Pack(uint high, uint low) => ((ulong)high << 32) | low;
}

/// <summary>
/// Raw COM calls by vtable slot. Slots are counted from 0 after IUnknown (QueryInterface, AddRef, Release occupy 0-2),
/// in the order of the Media Foundation headers:
/// IMFAttributes has 30 methods (slots 3-32) and IMFMediaType and IMFSample inherit them; IMFSample adds 14 more;
/// IMFMediaBuffer has 5; IMFSinkWriter has 11.
/// </summary>
internal static unsafe class MediaFoundationCalls
{
    // IMFAttributes (inherited by IMFMediaType and IMFSample): index in mfobjects.h + 3.
    private const int AttributesSetUint32 = 3 + 18;
    private const int AttributesSetUint64 = 3 + 19;
    private const int AttributesSetGuid = 3 + 21;

    // IMFSample: its own methods start after the 30 IMFAttributes slots.
    private const int SampleSetTime = 3 + 30 + 3;      // SetSampleTime
    private const int SampleSetDuration = 3 + 30 + 5;  // SetSampleDuration
    private const int SampleAddBuffer = 3 + 30 + 9;    // AddBuffer

    // IMFMediaBuffer.
    private const int BufferLock = 3;
    private const int BufferUnlock = 4;
    private const int BufferSetCurrentLength = 6;

    // IMFSinkWriter.
    private const int WriterAddStream = 3;
    private const int WriterSetInputMediaType = 4;
    private const int WriterBeginWriting = 5;
    private const int WriterWriteSample = 6;
    private const int WriterFinalize = 11;

    public static IntPtr Slot(IntPtr obj, int slot) => Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size);

    public static uint Release(IntPtr obj)
    {
        if (obj == IntPtr.Zero) return 0;
        return ((delegate* unmanaged<IntPtr, uint>)Slot(obj, 2))(obj);
    }

    public static int SetUInt32(IntPtr attributes, Guid key, uint value) =>
        ((delegate* unmanaged<IntPtr, Guid*, uint, int>)Slot(attributes, AttributesSetUint32))(attributes, &key, value);

    public static int SetUInt64(IntPtr attributes, Guid key, ulong value) =>
        ((delegate* unmanaged<IntPtr, Guid*, ulong, int>)Slot(attributes, AttributesSetUint64))(attributes, &key, value);

    public static int SetGuid(IntPtr attributes, Guid key, Guid value) =>
        ((delegate* unmanaged<IntPtr, Guid*, Guid*, int>)Slot(attributes, AttributesSetGuid))(attributes, &key, &value);

    public static int SetSampleTime(IntPtr sample, long hundredNanoseconds) =>
        ((delegate* unmanaged<IntPtr, long, int>)Slot(sample, SampleSetTime))(sample, hundredNanoseconds);

    public static int SetSampleDuration(IntPtr sample, long hundredNanoseconds) =>
        ((delegate* unmanaged<IntPtr, long, int>)Slot(sample, SampleSetDuration))(sample, hundredNanoseconds);

    public static int AddBuffer(IntPtr sample, IntPtr buffer) =>
        ((delegate* unmanaged<IntPtr, IntPtr, int>)Slot(sample, SampleAddBuffer))(sample, buffer);

    public static int Lock(IntPtr buffer, out IntPtr data)
    {
        byte* pointer = null;
        int hr = ((delegate* unmanaged<IntPtr, byte**, uint*, uint*, int>)Slot(buffer, BufferLock))(buffer, &pointer, null, null);
        data = (IntPtr)pointer;
        return hr;
    }

    public static int Unlock(IntPtr buffer) =>
        ((delegate* unmanaged<IntPtr, int>)Slot(buffer, BufferUnlock))(buffer);

    public static int SetCurrentLength(IntPtr buffer, uint length) =>
        ((delegate* unmanaged<IntPtr, uint, int>)Slot(buffer, BufferSetCurrentLength))(buffer, length);

    public static int AddStream(IntPtr writer, IntPtr mediaType, out uint streamIndex)
    {
        uint index = 0;
        int hr = ((delegate* unmanaged<IntPtr, IntPtr, uint*, int>)Slot(writer, WriterAddStream))(writer, mediaType, &index);
        streamIndex = index;
        return hr;
    }

    public static int SetInputMediaType(IntPtr writer, uint streamIndex, IntPtr mediaType) =>
        ((delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, int>)Slot(writer, WriterSetInputMediaType))(writer, streamIndex, mediaType, IntPtr.Zero);

    public static int BeginWriting(IntPtr writer) =>
        ((delegate* unmanaged<IntPtr, int>)Slot(writer, WriterBeginWriting))(writer);

    public static int WriteSample(IntPtr writer, uint streamIndex, IntPtr sample) =>
        ((delegate* unmanaged<IntPtr, uint, IntPtr, int>)Slot(writer, WriterWriteSample))(writer, streamIndex, sample);

    public static int FinalizeWriter(IntPtr writer) =>
        ((delegate* unmanaged<IntPtr, int>)Slot(writer, WriterFinalize))(writer);
}
