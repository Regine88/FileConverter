// <copyright file="InstallerPackageVerifier.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Core
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;

    /// <summary>
    /// Input for installer integrity verification (SHA-256 + Authenticode + publisher identity).
    /// </summary>
    public sealed class InstallerVerificationRequest
    {
        public string InstallerPath { get; set; }

        /// <summary>Required lowercase or uppercase hex SHA-256 of the package bytes.</summary>
        public string ExpectedSha256 { get; set; }

        /// <summary>Required substring that must appear in the Authenticode certificate subject.</summary>
        public string ExpectedPublisher { get; set; }

        /// <summary>
        /// Optional allow-list of certificate SHA-1 thumbprints (no spaces). Either current or previous
        /// thumbprint may match to support rotation.
        /// </summary>
        public IList<string> AllowedCertificateThumbprints { get; set; }

        /// <summary>
        /// When true (default), offline-only revocation failures may be accepted after WinVerifyTrust
        /// has already validated the signature cryptographically. All other chain failures fail closed.
        /// </summary>
        public bool AllowOfflineRevocationUnknown { get; set; } = true;
    }

    public sealed class InstallerVerificationResult
    {
        public bool Success { get; set; }

        public string FailureReason { get; set; }

        public string CertificateSubject { get; set; }

        public string CertificateThumbprint { get; set; }
    }

    /// <summary>
    /// Production verifier used by UpgradeService and unit tests.
    /// </summary>
    public static class InstallerPackageVerifier
    {
        // WINTRUST_ACTION_GENERIC_VERIFY_V2
        private static readonly Guid GenericVerifyV2 = new Guid("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");

        public static InstallerVerificationResult Verify(InstallerVerificationRequest request)
        {
            if (request == null)
            {
                return Fail("Verification request is null.");
            }

            if (string.IsNullOrWhiteSpace(request.InstallerPath))
            {
                return Fail("Installer path is missing.");
            }

            if (!File.Exists(request.InstallerPath))
            {
                return Fail($"Installer file not found: {request.InstallerPath}");
            }

            if (!FileExtension.IsInstallerExtension(request.InstallerPath))
            {
                return Fail($"Installer has unexpected extension: {Path.GetExtension(request.InstallerPath)}");
            }

            string expectedHash = request.ExpectedSha256?.Trim();
            if (string.IsNullOrEmpty(expectedHash))
            {
                return Fail("Installer SHA-256 is not published; refusing to run untrusted package.");
            }

            string actualHash = FileHash.ComputeSha256Hex(request.InstallerPath);
            if (!FileHash.Sha256Equals(expectedHash, actualHash))
            {
                return Fail("Installer SHA-256 does not match the published value.");
            }

            string expectedPublisher = request.ExpectedPublisher?.Trim();
            if (string.IsNullOrEmpty(expectedPublisher))
            {
                return Fail("Installer publisher identity is not published; refusing to run untrusted package.");
            }

            if (!WinVerifyTrustFile(request.InstallerPath, out string trustError))
            {
                return Fail(trustError ?? "WinVerifyTrust rejected the installer Authenticode signature.");
            }

            if (!TryReadSignerCertificate(request.InstallerPath, out X509Certificate2 cert, out string certError))
            {
                return Fail(certError ?? "Unable to read Authenticode signer certificate.");
            }

            using (cert)
            {
                if (!ValidateCertificateChain(cert, request.AllowOfflineRevocationUnknown, out string chainError))
                {
                    return Fail(chainError);
                }

                string subject = cert.Subject ?? string.Empty;
                if (subject.IndexOf(expectedPublisher, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return Fail($"Installer publisher '{subject}' does not match expected '{expectedPublisher}'.");
                }

                string thumbprint = (cert.Thumbprint ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
                if (request.AllowedCertificateThumbprints != null && request.AllowedCertificateThumbprints.Count > 0)
                {
                    bool thumbOk = false;
                    foreach (string allowed in request.AllowedCertificateThumbprints)
                    {
                        if (string.IsNullOrWhiteSpace(allowed))
                        {
                            continue;
                        }

                        if (string.Equals(allowed.Replace(" ", string.Empty), thumbprint, StringComparison.OrdinalIgnoreCase))
                        {
                            thumbOk = true;
                            break;
                        }
                    }

                    if (!thumbOk)
                    {
                        return Fail($"Installer certificate thumbprint '{thumbprint}' is not in the allowed list.");
                    }
                }

                return new InstallerVerificationResult
                {
                    Success = true,
                    CertificateSubject = subject,
                    CertificateThumbprint = thumbprint
                };
            }
        }

        public static void TryDeletePath(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                else if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // Best-effort cleanup of untrusted or leftover packages.
            }
        }

        private static InstallerVerificationResult Fail(string reason)
        {
            return new InstallerVerificationResult
            {
                Success = false,
                FailureReason = reason
            };
        }

        private static bool ValidateCertificateChain(X509Certificate2 cert, bool allowOfflineRevocationUnknown, out string error)
        {
            error = null;
            using (X509Chain chain = new X509Chain())
            {
                chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

                bool built = chain.Build(cert);
                if (built)
                {
                    return true;
                }

                // Fail closed by default. Optionally tolerate offline revocation only.
                foreach (X509ChainStatus status in chain.ChainStatus)
                {
                    if (status.Status == X509ChainStatusFlags.NoError)
                    {
                        continue;
                    }

                    bool offlineRevocation =
                        status.Status == X509ChainStatusFlags.OfflineRevocation ||
                        status.Status == X509ChainStatusFlags.RevocationStatusUnknown;

                    if (allowOfflineRevocationUnknown && offlineRevocation)
                    {
                        continue;
                    }

                    error = $"Certificate chain validation failed: {status.Status} ({status.StatusInformation?.Trim()})";
                    return false;
                }

                // Only offline-revocation issues remained.
                if (allowOfflineRevocationUnknown)
                {
                    return true;
                }

                error = "Certificate chain validation failed.";
                return false;
            }
        }

        private static bool TryReadSignerCertificate(string filePath, out X509Certificate2 cert, out string error)
        {
            cert = null;
            error = null;
            try
            {
                X509Certificate raw = X509Certificate.CreateFromSignedFile(filePath);
                cert = new X509Certificate2(raw);
                return true;
            }
            catch (CryptographicException)
            {
                error = "Installer is not Authenticode-signed or the signature is invalid.";
                return false;
            }
            catch (Exception exception)
            {
                error = $"Failed to read Authenticode signature: {exception.Message}";
                return false;
            }
        }

        private static bool WinVerifyTrustFile(string filePath, out string error)
        {
            error = null;

            WinTrustFileInfo fileInfo = new WinTrustFileInfo(filePath);
            WinTrustData data = new WinTrustData(fileInfo);

            try
            {
                int result = NativeMethods.WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, data);
                // 0 = trusted
                if (result == 0)
                {
                    return true;
                }

                error = $"WinVerifyTrust failed with code 0x{result:X8}.";
                return false;
            }
            catch (Exception exception)
            {
                error = $"WinVerifyTrust threw: {exception.Message}";
                return false;
            }
            finally
            {
                data.Dispose();
                fileInfo.Dispose();
            }
        }

        private static class NativeMethods
        {
            [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
            public static extern int WinVerifyTrust(
                IntPtr hwnd,
                [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
                WinTrustData pWVTData);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class WinTrustFileInfo : IDisposable
        {
            private uint structSize = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo));
            private IntPtr pszFilePath;
            private IntPtr hFile = IntPtr.Zero;
            private IntPtr pgKnownSubject = IntPtr.Zero;

            public WinTrustFileInfo(string filePath)
            {
                this.pszFilePath = Marshal.StringToCoTaskMemAuto(filePath);
            }

            public void Dispose()
            {
                if (this.pszFilePath != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(this.pszFilePath);
                    this.pszFilePath = IntPtr.Zero;
                }
            }
        }

        private enum WinTrustDataUIChoice : uint
        {
            All = 1,
            None = 2,
            NoBad = 3,
            NoGood = 4
        }

        private enum WinTrustDataRevocationChecks : uint
        {
            None = 0x00000000,
            WholeChain = 0x00000001
        }

        private enum WinTrustDataChoice : uint
        {
            File = 1,
            Catalog = 2,
            Blob = 3,
            Signer = 4,
            Certificate = 5
        }

        private enum WinTrustDataStateAction : uint
        {
            Ignore = 0x00000000,
            Verify = 0x00000001,
            Close = 0x00000002,
            AutoCache = 0x00000003,
            AutoCacheFlush = 0x00000004
        }

        [Flags]
        private enum WinTrustDataProvFlags : uint
        {
            UseIe4TrustFlag = 0x00000001,
            NoIe4ChainFlag = 0x00000002,
            NoPolicyUsageFlag = 0x00000004,
            RevocationCheckNone = 0x00000010,
            RevocationCheckEndCert = 0x00000020,
            RevocationCheckChain = 0x00000040,
            RevocationCheckChainExcludeRoot = 0x00000080,
            SaferFlag = 0x00000100,
            HashOnlyFlag = 0x00000200,
            UseDefaultOsverCheck = 0x00000400,
            LifetimeSigningFlag = 0x00000800,
            CacheOnlyUrlRetrieval = 0x00001000,
            DisableMD2andMD4 = 0x00002000
        }

        private enum WinTrustDataUIContext : uint
        {
            Execute = 0,
            Install = 1
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class WinTrustData : IDisposable
        {
            private uint structSize = (uint)Marshal.SizeOf(typeof(WinTrustData));
            private IntPtr policyCallbackData = IntPtr.Zero;
            private IntPtr SIPClientData = IntPtr.Zero;
            private WinTrustDataUIChoice uiChoice = WinTrustDataUIChoice.None;
            private WinTrustDataRevocationChecks revocationChecks = WinTrustDataRevocationChecks.WholeChain;
            private WinTrustDataChoice unionChoice = WinTrustDataChoice.File;
            private IntPtr fileInfoPtr;
            private WinTrustDataStateAction stateAction = WinTrustDataStateAction.Ignore;
            private IntPtr stateData = IntPtr.Zero;
            private string URLReference = null;
            private WinTrustDataProvFlags provFlags = WinTrustDataProvFlags.RevocationCheckChainExcludeRoot | WinTrustDataProvFlags.DisableMD2andMD4;
            private WinTrustDataUIContext UIContext = WinTrustDataUIContext.Execute;

            public WinTrustData(WinTrustFileInfo fileInfo)
            {
                this.fileInfoPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WinTrustFileInfo)));
                Marshal.StructureToPtr(fileInfo, this.fileInfoPtr, false);
            }

            public void Dispose()
            {
                if (this.fileInfoPtr != IntPtr.Zero)
                {
                    Marshal.DestroyStructure(this.fileInfoPtr, typeof(WinTrustFileInfo));
                    Marshal.FreeCoTaskMem(this.fileInfoPtr);
                    this.fileInfoPtr = IntPtr.Zero;
                }
            }
        }
    }
}
