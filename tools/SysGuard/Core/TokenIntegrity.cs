using System.Runtime.InteropServices;

namespace SysGuard;

/// <summary>
/// Nível de integridade do processo atual. O app roda em integridade média quando o usuário o abre
/// normalmente; o sandbox de desenvolvimento roda em integridade baixa, onde pipes de integridade
/// média são inacessíveis. O selftest usa isso para pular o teste de pipe no ambiente restrito.
/// </summary>
internal static class TokenIntegrity
{
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const int LowRid = 0x1000;

    /// <summary>True em integridade baixa ou não confiável (integralidade média é o normal).</summary>
    public static bool IsLowOrLess() => CurrentRid() <= LowRid;

    private static int CurrentRid()
    {
        IntPtr token = IntPtr.Zero;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token) || token == IntPtr.Zero)
                return int.MaxValue;

            int size = 256;
            buffer = Marshal.AllocHGlobal(size);
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out int needed))
            {
                size = Math.Max(needed, 64);
                Marshal.FreeHGlobal(buffer);
                buffer = Marshal.AllocHGlobal(size);
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out _))
                    return int.MaxValue;
            }

            // TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES { PSID Sid; DWORD Attributes } }
            IntPtr sid = Marshal.PtrToStructure<SidAndAttributes>(buffer).Sid;
            if (sid == IntPtr.Zero) return int.MaxValue;

            // Layout de um SID: Revision (byte 0), SubAuthorityCount (byte 1),
            // IdentifierAuthority (bytes 2..7), SubAuthority (a partir do byte 8).
            int count = Marshal.ReadByte(sid, 1);
            if (count is < 1 or > 15) return int.MaxValue;
            return Marshal.ReadInt32(sid, 8 + (4 * (count - 1)));
        }
        catch (Exception ex)
        {
            Log.Info("Integridade: falha ao ler o token: " + ex.Message);
            return int.MaxValue;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            if (token != IntPtr.Zero) CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr token, int informationClass, IntPtr information, int informationLength, out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
