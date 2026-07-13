// <copyright file="FfmpegProgressParserTests.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Tests
{
    using System;

    using FileConverter.Core;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class FfmpegProgressParserTests
    {
        [TestMethod]
        public void Parse_duration_line()
        {
            var result = FfmpegProgressParser.Parse(
                "  Duration: 00:01:30.50, start: 0.000000, bitrate: 128 kb/s",
                TimeSpan.Zero,
                null,
                null);

            Assert.IsTrue(result.HasDuration);
            Assert.AreEqual(TimeSpan.FromMilliseconds((90 * 1000) + 500), result.Duration);
        }

        [TestMethod]
        public void Parse_out_time_ms_progress()
        {
            TimeSpan duration = TimeSpan.FromSeconds(10);
            // 5 seconds in microseconds
            var result = FfmpegProgressParser.Parse("out_time_ms=5000000", duration, null, null);
            Assert.IsTrue(result.HasProgress);
            Assert.AreEqual(0.5f, result.ProgressRatio, 0.001f);
        }

        [TestMethod]
        public void Parse_detects_error_without_false_positive_on_path()
        {
            var ok = FfmpegProgressParser.Parse(
                @"Input file C:\ErrorVideos\clip.mp4 opened",
                TimeSpan.Zero,
                @"C:\ErrorVideos\clip.mp4",
                @"C:\out.mp4");
            Assert.IsFalse(ok.LooksLikeError);

            var bad = FfmpegProgressParser.Parse(
                "Error while opening encoder",
                TimeSpan.Zero,
                @"C:\in.mp4",
                @"C:\out.mp4");
            Assert.IsTrue(bad.LooksLikeError);
        }
    }
}
