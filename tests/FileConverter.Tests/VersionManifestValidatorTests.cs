// <copyright file="VersionManifestValidatorTests.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Tests
{
    using System;
    using System.IO;

    using FileConverter.Core;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class VersionManifestValidatorTests
    {
        [TestMethod]
        public void ValidateFile_accepts_complete_manifest()
        {
            string path = WriteTempManifest(
                @"<Version>
  <Latest Major=""2"" Minor=""2"" Patch=""0""/>
  <URL>https://example.com/setup.msi</URL>
  <SHA256>0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef</SHA256>
  <Publisher>CN=Example Publisher</Publisher>
</Version>");

            try
            {
                var result = VersionManifestValidator.ValidateFile(path, requireSha256AndPublisher: true);
                Assert.IsTrue(result.IsValid, result.Error);
                Assert.AreEqual("2.2", result.SemanticVersion);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ValidateFile_rejects_empty_sha_when_required()
        {
            string path = WriteTempManifest(
                @"<Version>
  <Latest Major=""2"" Minor=""2"" Patch=""0""/>
  <URL>https://example.com/setup.msi</URL>
  <SHA256></SHA256>
  <Publisher>CN=Example</Publisher>
</Version>");

            try
            {
                var result = VersionManifestValidator.ValidateFile(path, requireSha256AndPublisher: true);
                Assert.IsFalse(result.IsValid);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string WriteTempManifest(string xml)
        {
            string path = Path.Combine(Path.GetTempPath(), "fc-version-" + Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(path, xml);
            return path;
        }
    }
}
