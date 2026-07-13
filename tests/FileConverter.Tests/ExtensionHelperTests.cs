// <copyright file="ExtensionHelperTests.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Tests
{
    using FileConverter.Core;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Exercises production <see cref="FileExtension"/> helpers (not a reimplementation).
    /// </summary>
    [TestClass]
    public class ExtensionHelperTests
    {
        [TestMethod]
        public void Normalize_strips_dot_and_lowercases()
        {
            Assert.AreEqual("png", FileExtension.Normalize(".PNG"));
            Assert.AreEqual("jpg", FileExtension.Normalize("JPG"));
        }

        [TestMethod]
        public void Normalize_empty_or_null_returns_empty()
        {
            Assert.AreEqual(string.Empty, FileExtension.Normalize(null));
            Assert.AreEqual(string.Empty, FileExtension.Normalize(string.Empty));
            Assert.AreEqual(string.Empty, FileExtension.Normalize("."));
        }

        [TestMethod]
        public void FromPath_handles_missing_extension()
        {
            Assert.AreEqual(string.Empty, FileExtension.FromPath(@"C:\folder\file"));
            Assert.AreEqual(string.Empty, FileExtension.FromPath(@"C:\folder\file."));
            Assert.AreEqual(string.Empty, FileExtension.FromPath(null));
        }

        [TestMethod]
        public void FromPath_handles_multi_dot_and_unicode()
        {
            Assert.AreEqual("gz", FileExtension.FromPath(@"C:\a\archive.tar.gz"));
            Assert.AreEqual("txt", FileExtension.FromPath(@"C:\docs\测试.TXT"));
        }

        [TestMethod]
        public void FromPath_handles_unc_paths()
        {
            Assert.AreEqual("mp4", FileExtension.FromPath(@"\\server\share\video.MP4"));
        }

        [TestMethod]
        public void IsInstallerExtension_accepts_msi_and_exe_only()
        {
            Assert.IsTrue(FileExtension.IsInstallerExtension("setup.msi"));
            Assert.IsTrue(FileExtension.IsInstallerExtension(@"C:\tmp\a.EXE"));
            Assert.IsFalse(FileExtension.IsInstallerExtension("payload.txt"));
            Assert.IsFalse(FileExtension.IsInstallerExtension("archive.tar.gz"));
        }
    }
}
