using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace SaveVaultApp.Services
{
    /// <summary>
    /// Encrypts small secrets (e.g. the auth token) at rest so they are not stored
    /// as readable plaintext in settings.json.
    ///
    /// On Windows this uses DPAPI (CurrentUser scope) via P/Invoke — no extra NuGet
    /// package — so only the same Windows user account can decrypt the value. On
    /// other platforms (the app currently ships Windows-only) it falls back to
    /// storing the value tagged-but-unprotected, and logs a warning. Stored values
    /// are prefixed so the format is always unambiguous and older plaintext values
    /// migrate transparently.
    /// </summary>
    public static class CredentialProtector
    {
        private const string DpapiPrefix = "dpapi:"; // Windows DPAPI-protected, base64
        private const string PlainPrefix = "plain:"; // stored as-is (fallback)

        /// <summary>Encrypt a value for storage. Null/empty pass through unchanged.</summary>
        public static string? Protect(string? plaintext)
        {
            if (string.IsNullOrEmpty(plaintext))
                return plaintext;

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var encrypted = DpapiTransform(Encoding.UTF8.GetBytes(plaintext), protect: true);
                    return DpapiPrefix + Convert.ToBase64String(encrypted);
                }
            }
            catch (Exception ex)
            {
                TryWarn($"Credential encryption failed, storing unprotected: {ex.Message}");
            }

            // Non-Windows, or DPAPI failed: store tagged but unprotected.
            return PlainPrefix + plaintext;
        }

        /// <summary>Decrypt a stored value. Returns null if it cannot be decrypted.</summary>
        public static string? Unprotect(string? stored)
        {
            if (string.IsNullOrEmpty(stored))
                return stored;

            try
            {
                if (stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
                {
                    if (!OperatingSystem.IsWindows())
                        return null; // a DPAPI blob cannot be decrypted off Windows
                    var encrypted = Convert.FromBase64String(stored.Substring(DpapiPrefix.Length));
                    return Encoding.UTF8.GetString(DpapiTransform(encrypted, protect: false));
                }

                if (stored.StartsWith(PlainPrefix, StringComparison.Ordinal))
                    return stored.Substring(PlainPrefix.Length);

                // Legacy value written before encryption existed: treat as plaintext
                // (it will be re-written in protected form on the next save).
                return stored;
            }
            catch (Exception ex)
            {
                TryWarn($"Credential decryption failed: {ex.Message}");
                return null;
            }
        }

        private static void TryWarn(string message)
        {
            try { LoggingService.Instance?.Warning(message); } catch { /* logging must never throw here */ }
        }

        // ----- Windows DPAPI (crypt32.dll) via P/Invoke -----

        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [SupportedOSPlatform("windows")]
        private static byte[] DpapiTransform(byte[] data, bool protect)
        {
            var inBlob = new DATA_BLOB();
            var outBlob = new DATA_BLOB();
            try
            {
                inBlob.cbData = data.Length;
                inBlob.pbData = Marshal.AllocHGlobal(data.Length);
                Marshal.Copy(data, 0, inBlob.pbData, data.Length);

                bool ok = protect
                    ? CryptProtectData(ref inBlob, "SaveVault", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob)
                    : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob);

                if (!ok)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

                var result = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                return result;
            }
            finally
            {
                // inBlob was allocated by us (HGlobal); outBlob by DPAPI (LocalAlloc).
                if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }
}
