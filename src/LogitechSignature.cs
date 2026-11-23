using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace GHubStartupFix
{
    internal static class LogitechSignature
    {
        internal static bool Verify(string path)
        {
            var file = new TrustFile
            {
                Size = (uint)Marshal.SizeOf(typeof(TrustFile)),
                Path = path
            };
            var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
            IntPtr filePtr = Marshal.AllocHGlobal((int)file.Size);
            Marshal.StructureToPtr(file, filePtr, false);
            var data = new TrustData
            {
                Size = (uint)Marshal.SizeOf(typeof(TrustData)),
                UiChoice = 2,
                UnionChoice = 1,
                File = filePtr,
                StateAction = 1,
                ProviderFlags = 0x1000 // 使用本地证书缓存验证。
            };
            try
            {
                // 先验证 Authenticode，再读取这次验证所使用的签名证书。
                if (WinVerifyTrust(IntPtr.Zero, ref action, ref data) != 0 || data.State == IntPtr.Zero)
                    return false;
                IntPtr provider = WTHelperProvDataFromStateData(data.State);
                if (provider == IntPtr.Zero)
                    return false;
                IntPtr signer = WTHelperGetProvSignerFromChain(provider, 0, false, 0);
                if (signer == IntPtr.Zero)
                    return false;
                IntPtr cert = WTHelperGetProvCertFromChain(signer, 0);
                if (cert == IntPtr.Zero)
                    return false;

                var verified = (ProviderCertificate)Marshal.PtrToStructure(cert, typeof(ProviderCertificate));
                if (verified.Context == IntPtr.Zero)
                    return false;
                using (var publisher = new X509Certificate2(verified.Context))
                    return String.Equals(publisher.GetNameInfo(X509NameType.SimpleName, false),
                        "Logitech Inc", StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (data.State != IntPtr.Zero)
                {
                    data.StateAction = 2;
                    WinVerifyTrust(IntPtr.Zero, ref action, ref data);
                }
                Marshal.DestroyStructure(filePtr, typeof(TrustFile));
                Marshal.FreeHGlobal(filePtr);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TrustFile
        {
            internal uint Size;
            internal string Path;
            internal IntPtr FileHandle, KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TrustData
        {
            internal uint Size;
            internal IntPtr PolicyCallbackData, SipClientData;
            internal uint UiChoice, RevocationChecks, UnionChoice;
            internal IntPtr File;
            internal uint StateAction;
            internal IntPtr State, UrlReference;
            internal uint ProviderFlags, UiContext;
            internal IntPtr SignatureSettings;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProviderCertificate
        {
            internal uint Size;
            internal IntPtr Context;
        }

        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);

        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);

        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint index, bool counterSigner, uint counterIndex);

        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint index);
    }
}
