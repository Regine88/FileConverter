// <copyright file="InstallerIntegrityTests.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Tests
{
    using System;
    using System.IO;
    using System.Text;

    using FileConverter.Core;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Calls production <see cref="InstallerPackageVerifier"/> / <see cref="FileHash"/> paths.
    /// </summary>
    [TestClass]
    public class InstallerIntegrityTests
    {
        [TestMethod]
        public void FileHash_of_known_content_is_stable()
        {
            string path = Path.Combine(Path.GetTempPath(), "fc-hash-test-" + Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                File.WriteAllBytes(path, Encoding.UTF8.GetBytes("FileConverter-integrity-probe"));
                string hash = FileHash.ComputeSha256Hex(path);
                Assert.AreEqual(64, hash.Length);
                Assert.IsTrue(FileHash.Sha256Equals(hash, FileHash.ComputeSha256Hex(path)));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [TestMethod]
        public void FileHash_mismatch_is_detectable()
        {
            string path = Path.Combine(Path.GetTempPath(), "fc-hash-test-" + Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                File.WriteAllText(path, "content-a");
                string hashA = FileHash.ComputeSha256Hex(path);
                File.WriteAllText(path, "content-b");
                string hashB = FileHash.ComputeSha256Hex(path);
                Assert.IsFalse(FileHash.Sha256Equals(hashA, hashB));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [TestMethod]
        public void Verify_rejects_missing_sha256()
        {
            string path = CreateTempMsiBytes();
            try
            {
                var result = InstallerPackageVerifier.Verify(new InstallerVerificationRequest
                {
                    InstallerPath = path,
                    ExpectedSha256 = null,
                    ExpectedPublisher = "CN=Test"
                });
                Assert.IsFalse(result.Success);
                StringAssert.Contains(result.FailureReason, "SHA-256");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void Verify_rejects_missing_publisher()
        {
            string path = CreateTempMsiBytes();
            try
            {
                string hash = FileHash.ComputeSha256Hex(path);
                var result = InstallerPackageVerifier.Verify(new InstallerVerificationRequest
                {
                    InstallerPath = path,
                    ExpectedSha256 = hash,
                    ExpectedPublisher = "  "
                });
                Assert.IsFalse(result.Success);
                StringAssert.Contains(result.FailureReason, "publisher");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void Verify_rejects_sha256_mismatch()
        {
            string path = CreateTempMsiBytes();
            try
            {
                var result = InstallerPackageVerifier.Verify(new InstallerVerificationRequest
                {
                    InstallerPath = path,
                    ExpectedSha256 = new string('a', 64),
                    ExpectedPublisher = "CN=Test"
                });
                Assert.IsFalse(result.Success);
                StringAssert.Contains(result.FailureReason, "SHA-256");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void Verify_rejects_wrong_extension()
        {
            string path = Path.Combine(Path.GetTempPath(), "fc-payload-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, "not-an-installer");
            try
            {
                var result = InstallerPackageVerifier.Verify(new InstallerVerificationRequest
                {
                    InstallerPath = path,
                    ExpectedSha256 = FileHash.ComputeSha256Hex(path),
                    ExpectedPublisher = "CN=Test"
                });
                Assert.IsFalse(result.Success);
                StringAssert.Contains(result.FailureReason, "extension");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void Verify_rejects_unsigned_package_with_matching_hash()
        {
            string path = CreateTempMsiBytes();
            try
            {
                string hash = FileHash.ComputeSha256Hex(path);
                var result = InstallerPackageVerifier.Verify(new InstallerVerificationRequest
                {
                    InstallerPath = path,
                    ExpectedSha256 = hash,
                    ExpectedPublisher = "CN=Anything"
                });
                Assert.IsFalse(result.Success);
                Assert.IsTrue(
                    result.FailureReason.IndexOf("WinVerifyTrust", StringComparison.OrdinalIgnoreCase) >= 0
                    || result.FailureReason.IndexOf("Authenticode", StringComparison.OrdinalIgnoreCase) >= 0
                    || result.FailureReason.IndexOf("signature", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string CreateTempMsiBytes()
        {
            string path = Path.Combine(Path.GetTempPath(), "fc-setup-" + Guid.NewGuid().ToString("N") + ".msi");
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("not-a-real-msi-payload"));
            return path;
        }
    }
}
