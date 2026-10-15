using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Island.Windows.Storage;

/// <summary>DPAPI, scoped to the current Windows user. Plaintext imports remain readable.</summary>
internal static class UserSecret
{
    private const string Prefix = "dpapi:v1:";
    public static bool IsProtected(string text) => text.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Protect(string text)
    {
        if (text.Length == 0 || IsProtected(text)) return text;
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        try { return Prefix + Convert.ToBase64String(Transform(bytes, protect: true)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static string Unprotect(string text)
    {
        if (!IsProtected(text)) return text;
        byte[] bytes = Transform(Convert.FromBase64String(text[Prefix.Length..]), protect: false);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static unsafe byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            bool ok = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
            byte[] result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(new Span<byte>(input.Data.ToPointer(), input.Length));
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            {
                CryptographicOperations.ZeroMemory(new Span<byte>(output.Data.ToPointer(), output.Length));
                LocalFree(output.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
