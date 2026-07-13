// <copyright file="VersionTests.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Tests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class VersionTests
    {
        [TestMethod]
        public void CompareTo_Equal_versions_returns_zero()
        {
            var a = new Version { Major = 2, Minor = 2, Patch = 0 };
            var b = new Version { Major = 2, Minor = 2, Patch = 0 };
            Assert.AreEqual(0, a.CompareTo(b));
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
        }

        [TestMethod]
        public void CompareTo_higher_major_is_greater()
        {
            var a = new Version { Major = 3, Minor = 0, Patch = 0 };
            var b = new Version { Major = 2, Minor = 9, Patch = 9 };
            Assert.IsTrue(a > b);
            Assert.IsTrue(b < a);
        }

        [TestMethod]
        public void CompareTo_higher_minor_is_greater()
        {
            var a = new Version { Major = 2, Minor = 3, Patch = 0 };
            var b = new Version { Major = 2, Minor = 2, Patch = 99 };
            Assert.IsTrue(a > b);
        }

        [TestMethod]
        public void CompareTo_higher_patch_is_greater()
        {
            var a = new Version { Major = 2, Minor = 2, Patch = 1 };
            var b = new Version { Major = 2, Minor = 2, Patch = 0 };
            Assert.IsTrue(a > b);
            Assert.IsTrue(a >= b);
            Assert.IsTrue(b <= a);
        }

        [TestMethod]
        public void ToString_omits_zero_patch()
        {
            var v = new Version { Major = 2, Minor = 2, Patch = 0 };
            Assert.AreEqual("2.2", v.ToString());
        }

        [TestMethod]
        public void ToString_includes_nonzero_patch()
        {
            var v = new Version { Major = 2, Minor = 2, Patch = 1 };
            Assert.AreEqual("2.2.1", v.ToString());
        }
    }
}
