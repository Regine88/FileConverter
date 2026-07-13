// <copyright file="VersionManifestValidator.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Core
{
    using System;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Xml.Linq;

    /// <summary>
    /// Validates version.xml / version (x86).xml publishing requirements.
    /// </summary>
    public static class VersionManifestValidator
    {
        public sealed class Result
        {
            public bool IsValid { get; set; }

            public string Error { get; set; }

            public string SemanticVersion { get; set; }

            public string Sha256 { get; set; }

            public string Publisher { get; set; }

            public string Url { get; set; }
        }

        public static Result ValidateFile(string path, bool requireSha256AndPublisher = true)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return new Result { IsValid = false, Error = $"Manifest not found: {path}" };
            }

            try
            {
                XDocument doc = XDocument.Load(path);
                XElement root = doc.Root;
                if (root == null || !string.Equals(root.Name.LocalName, "Version", StringComparison.OrdinalIgnoreCase))
                {
                    return new Result { IsValid = false, Error = "Root element must be Version." };
                }

                XElement latest = root.Element("Latest");
                if (latest == null)
                {
                    return new Result { IsValid = false, Error = "Missing Latest element." };
                }

                int major = (int?)latest.Attribute("Major") ?? -1;
                int minor = (int?)latest.Attribute("Minor") ?? -1;
                int patch = (int?)latest.Attribute("Patch") ?? 0;
                if (major < 0 || minor < 0)
                {
                    return new Result { IsValid = false, Error = "Latest Major/Minor attributes are required." };
                }

                string sha = ((string)root.Element("SHA256") ?? string.Empty).Trim();
                string publisher = ((string)root.Element("Publisher") ?? string.Empty).Trim();
                string url = ((string)root.Element("URL") ?? string.Empty).Trim();

                if (string.IsNullOrEmpty(url))
                {
                    return new Result { IsValid = false, Error = "URL is required." };
                }

                if (requireSha256AndPublisher)
                {
                    if (string.IsNullOrEmpty(sha) || !Regex.IsMatch(sha, "^[A-Fa-f0-9]{64}$"))
                    {
                        return new Result { IsValid = false, Error = "SHA256 must be a non-empty 64-char hex digest for release manifests." };
                    }

                    if (string.IsNullOrEmpty(publisher))
                    {
                        return new Result { IsValid = false, Error = "Publisher must be non-empty for release manifests." };
                    }
                }

                return new Result
                {
                    IsValid = true,
                    SemanticVersion = patch == 0 ? $"{major}.{minor}" : $"{major}.{minor}.{patch}",
                    Sha256 = sha,
                    Publisher = publisher,
                    Url = url
                };
            }
            catch (Exception exception)
            {
                return new Result { IsValid = false, Error = exception.Message };
            }
        }
    }
}
