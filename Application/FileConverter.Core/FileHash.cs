// <copyright file="FileHash.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Core
{
    using System;
    using System.IO;
    using System.Security.Cryptography;

    public static class FileHash
    {
        public static string ComputeSha256Hex(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            using (FileStream stream = File.OpenRead(filePath))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        public static bool Sha256Equals(string expectedHex, string actualHex)
        {
            if (string.IsNullOrWhiteSpace(expectedHex) || string.IsNullOrWhiteSpace(actualHex))
            {
                return false;
            }

            return string.Equals(expectedHex.Trim(), actualHex.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
