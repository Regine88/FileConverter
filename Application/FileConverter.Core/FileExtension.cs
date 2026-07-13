// <copyright file="FileExtension.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Core
{
    using System;
    using System.IO;

    /// <summary>
    /// Shared extension normalization used by the app and unit tests (production code path).
    /// </summary>
    public static class FileExtension
    {
        /// <summary>
        /// Normalizes a file extension by removing a leading dot and converting to lower case.
        /// Returns an empty string when the extension is missing or null.
        /// </summary>
        public static string Normalize(string extensionOrPathExtension)
        {
            if (string.IsNullOrEmpty(extensionOrPathExtension))
            {
                return string.Empty;
            }

            return extensionOrPathExtension.TrimStart('.').ToLowerInvariant();
        }

        /// <summary>
        /// Returns the file extension without the leading dot, or an empty string when missing.
        /// </summary>
        public static string FromPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return string.Empty;
            }

            try
            {
                return Normalize(Path.GetExtension(filePath));
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        public static bool IsInstallerExtension(string pathOrExtension)
        {
            if (string.IsNullOrEmpty(pathOrExtension))
            {
                return false;
            }

            string ext;
            try
            {
                if (pathOrExtension.IndexOfAny(new[] { '\\', '/', ':' }) >= 0 || pathOrExtension.Contains("."))
                {
                    // Path or file name with extension (e.g. setup.msi, C:\a.exe).
                    ext = pathOrExtension.StartsWith(".", StringComparison.Ordinal) && pathOrExtension.IndexOfAny(new[] { '\\', '/' }) < 0
                        ? pathOrExtension
                        : Path.GetExtension(pathOrExtension);
                }
                else
                {
                    // Bare extension token without a leading dot (e.g. "msi").
                    ext = "." + pathOrExtension;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }

            return string.Equals(ext, ".msi", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase);
        }
    }
}
