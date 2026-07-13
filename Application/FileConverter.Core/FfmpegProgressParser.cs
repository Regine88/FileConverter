// <copyright file="FfmpegProgressParser.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Core
{
    using System;
    using System.Globalization;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Pure FFmpeg progress/duration line parser (testable without spawning processes).
    /// </summary>
    public static class FfmpegProgressParser
    {
        private static readonly Regex DurationRegex = new Regex(
            @"Duration:\s*([0-9][0-9]):([0-9][0-9]):([0-9][0-9])\.([0-9][0-9]),.*bitrate:\s*([0-9]+) kb\/s",
            RegexOptions.Compiled);

        private static readonly Regex ProgressRegex = new Regex(
            @"size=\s*([0-9]+).*time=([0-9][0-9]):([0-9][0-9]):([0-9][0-9]).([0-9][0-9])\s+bitrate=\s*([0-9]+.[0-9])",
            RegexOptions.Compiled);

        public struct ParseResult
        {
            public bool HasDuration;
            public TimeSpan Duration;
            public bool HasProgress;
            public TimeSpan Converted;
            public float ProgressRatio;
            public bool LooksLikeError;
            public string ErrorHint;
        }

        public static ParseResult Parse(string input, TimeSpan knownDuration, string inputPath, string outputPath)
        {
            ParseResult result = default;

            if (string.IsNullOrEmpty(input))
            {
                return result;
            }

            if (input.StartsWith("out_time_ms=", StringComparison.Ordinal) && knownDuration.Ticks > 0)
            {
                string value = input.Substring("out_time_ms=".Length).Trim();
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long microSeconds) && microSeconds >= 0)
                {
                    result.HasProgress = true;
                    result.Converted = TimeSpan.FromTicks(microSeconds * 10);
                    result.ProgressRatio = Clamp01(result.Converted.Ticks / (float)knownDuration.Ticks);
                }

                return result;
            }

            if (input.StartsWith("out_time=", StringComparison.Ordinal) && knownDuration.Ticks > 0)
            {
                string value = input.Substring("out_time=".Length).Trim();
                if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan parsed))
                {
                    result.HasProgress = true;
                    result.Converted = parsed;
                    result.ProgressRatio = Clamp01(result.Converted.Ticks / (float)knownDuration.Ticks);
                }

                return result;
            }

            Match match = DurationRegex.Match(input);
            if (match.Success && match.Groups.Count >= 6)
            {
                int hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                int minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                int seconds = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                int milliseconds = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) * 10;
                result.HasDuration = true;
                result.Duration = new TimeSpan(0, hours, minutes, seconds, milliseconds);
                return result;
            }

            if (knownDuration.Ticks > 0)
            {
                match = ProgressRegex.Match(input);
                if (match.Success && match.Groups.Count >= 7)
                {
                    int hours = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                    int minutes = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                    int seconds = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
                    int milliseconds = int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture) * 10;
                    result.HasProgress = true;
                    result.Converted = new TimeSpan(0, hours, minutes, seconds, milliseconds);
                    result.ProgressRatio = Clamp01(result.Converted.Ticks / (float)knownDuration.Ticks);
                    return result;
                }
            }

            string withoutNames = input;
            if (!string.IsNullOrEmpty(inputPath))
            {
                withoutNames = withoutNames.Replace(inputPath, string.Empty);
            }

            if (!string.IsNullOrEmpty(outputPath))
            {
                withoutNames = withoutNames.Replace(outputPath, string.Empty);
            }

            if (withoutNames.Contains("Exiting.") || withoutNames.Contains("Error") ||
                withoutNames.Contains("Unsupported dimensions") || withoutNames.Contains("No such file or directory"))
            {
                if (withoutNames.StartsWith("Error while decoding stream", StringComparison.Ordinal) &&
                    withoutNames.EndsWith("Invalid data found when processing input", StringComparison.Ordinal))
                {
                    // Transport stream broken frame at start is normal.
                }
                else
                {
                    result.LooksLikeError = true;
                    result.ErrorHint = input;
                }
            }

            return result;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            if (value > 1f)
            {
                return 1f;
            }

            return value;
        }
    }
}
