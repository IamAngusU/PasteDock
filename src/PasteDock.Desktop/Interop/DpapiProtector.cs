using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using PasteDock.Core.Storage;

namespace PasteDock.Desktop.Interop;

/// <summary>Current-user DPAPI. Does not use the machine-wide protection flag.</summary>
public sealed class DpapiProtector : IContentProtector
{
    public byte[] Protect(byte[] plaintext) => Transform(plaintext, true);
    public byte[] Unprotect(byte[] ciphertext) => Transform(ciphertext, false);

    private static unsafe byte[] Transform(byte[] input, bool protect)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI requires Windows.");
        var pinned = GCHandle.Alloc(input, GCHandleType.Pinned);
        var source = new DataBlob { Length = input.Length, Data = pinned.AddrOfPinnedObject() };
        var output = new DataBlob();
        try
        {
            var success = protect
                ? CryptProtectData(ref source, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref source, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new CryptographicException("Windows could not protect or unlock this local data.", new Win32Exception(Marshal.GetLastWin32Error()));
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (output.Data != IntPtr.Zero)
            {
                CryptographicOperations.ZeroMemory(new Span<byte>((void*)output.Data, output.Length));
                _ = LocalFree(output.Data);
            }
            pinned.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob data, string? description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob data, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
