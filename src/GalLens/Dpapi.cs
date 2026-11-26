using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace GalLens;

internal static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB { public int cbData; public IntPtr pbData; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DATA_BLOB pIn, string? desc, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DATA_BLOB pOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pIn, IntPtr desc, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DATA_BLOB pOut);

    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr h);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr h);

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var bytes = Encoding.UTF8.GetBytes(plain);
        var p = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, p, bytes.Length);
        var inBlob = new DATA_BLOB { cbData = bytes.Length, pbData = p };
        try
        {
            if (!CryptProtectData(ref inBlob, "GalLens", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var outBlob))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var res = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, res, 0, res.Length);
                return Convert.ToBase64String(res);
            }
            finally { LocalFree(outBlob.pbData); }
        }
        finally { Marshal.FreeHGlobal(p); }
    }

    public static string Unprotect(string cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return "";
        byte[] bytes;
        try { bytes = Convert.FromBase64String(cipher); } catch { return ""; }
        var p = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, p, bytes.Length);
        var inBlob = new DATA_BLOB { cbData = bytes.Length, pbData = p };
        try
        {
            if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var outBlob))
                return "";
            try
            {
                var res = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, res, 0, res.Length);
                return Encoding.UTF8.GetString(res);
            }
            finally { LocalFree(outBlob.pbData); }
        }
        finally { Marshal.FreeHGlobal(p); }
    }
}
