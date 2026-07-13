// <copyright file="UpgradeService.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using System.Xml;
    using System.Xml.Serialization;

    using CommunityToolkit.Mvvm.ComponentModel;

    using FileConverter.Annotations;
    using FileConverter.Core;
    using FileConverter.Diagnostics;

    public class UpgradeService : ObservableObject, IUpgradeService
    {
#if DEBUG
        private const string BaseURI = "https://raw.githubusercontent.com/Tichau/FileConverter/integration/";
#else
        private const string BaseURI = "https://raw.githubusercontent.com/Tichau/FileConverter/master/";
#endif

        [NotNull]
        private readonly WebClient webClient = new WebClient();

        private UpgradeVersionDescription upgradeVersionDescription;

        public UpgradeService()
        {
            this.UpgradeVersionDescription = new UpgradeVersionDescription();
            CleanupStaleUpgradeDirectories();
        }

        /// <summary>
        /// Removes leftover upgrade download folders older than 24 hours.
        /// </summary>
        public static void CleanupStaleUpgradeDirectories(TimeSpan? maxAge = null)
        {
            TimeSpan age = maxAge ?? TimeSpan.FromHours(24);
            string tempPath = Path.GetTempPath();
            DateTime cutoff = DateTime.UtcNow.Subtract(age);

            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(tempPath, "FileConverter-Upgrade-*");
            }
            catch
            {
                return;
            }

            foreach (string dir in dirs)
            {
                try
                {
                    if (Directory.GetCreationTimeUtc(dir) < cutoff)
                    {
                        InstallerPackageVerifier.TryDeletePath(dir);
                    }
                }
                catch
                {
                    // Ignore individual cleanup failures.
                }
            }
        }

        public event EventHandler<UpgradeVersionDescription> NewVersionAvailable;

        public UpgradeVersionDescription UpgradeVersionDescription
        {
            get => this.upgradeVersionDescription;
            private set
            {
                this.upgradeVersionDescription = value;
                this.OnPropertyChanged();
            }
        }
        
        public async Task<UpgradeVersionDescription> CheckForUpgrade()
        {
            try
            {
#if !DEBUG
                long fileTime = Registry.GetValue<long>(Registry.Keys.LastUpdateCheckDate);
                DateTime lastUpdateDateTime = DateTime.FromFileTime(fileTime);

                TimeSpan durationSinceLastUpdate = DateTime.Now.Subtract(lastUpdateDateTime);
                if (durationSinceLastUpdate <= new TimeSpan(1, 0, 0, 0))
                {
                    // Not due for a check yet — return without awaiting a null Task.
                    return null;
                }
#endif
            }
            catch (Exception exception)
            {
                Diagnostics.Debug.Log($"Failed to check upgrade schedule: {exception.Message}.");
                // Fall through and attempt a network check when registry values are invalid.
            }

            UpgradeVersionDescription versionDescription;
            try
            {
                versionDescription = await this.DownloadLatestVersionDescription().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Diagnostics.Debug.Log($"Failed to check upgrade: {exception.Message}.");
                return null;
            }

            if (versionDescription == null)
            {
                return null;
            }

            try
            {
                Registry.SetValue(Registry.Keys.LastUpdateCheckDate, DateTime.Now.ToFileTime());
            }
            catch (Exception exception)
            {
                Diagnostics.Debug.Log($"Failed to store last update check date: {exception.Message}.");
            }

            if (versionDescription.LatestVersion <= Application.ApplicationVersion)
            {
                return null;
            }

            versionDescription.State = UpgradeState.Available;
            this.UpgradeVersionDescription = versionDescription;

            this.NewVersionAvailable?.Invoke(this, versionDescription);
            return versionDescription;
        }

        public async Task<string> DownloadChangeLog()
        {
            if (this.UpgradeVersionDescription == null)
            {
                throw new ArgumentNullException(nameof(this.UpgradeVersionDescription));
            }

            this.UpgradeVersionDescription.ChangeLog = Properties.Resources.DownloadingChangeLog;

            Uri uri = new Uri(UpgradeService.BaseURI + "CHANGELOG.md");
            try
            {
                Task<Stream> openReadTaskAsync = this.webClient.OpenReadTaskAsync(uri);
                if (openReadTaskAsync == null)
                {
                    return null;
                }

                Stream stream = await openReadTaskAsync.ConfigureAwait(false);
                using (StreamReader reader = new StreamReader(stream))
                {
                    this.UpgradeVersionDescription.ChangeLog = reader.ReadToEnd();
                }
            }
            catch (Exception)
            {
                Debug.LogError("Error while retrieving change log.");
                return null;
            }

            return this.UpgradeVersionDescription.ChangeLog;
        }

        public async Task StartUpgrade()
        {
            if (this.UpgradeVersionDescription == null)
            {
                Debug.Log("Can't start upgrade because no check upgrade have been done.");
                return;
            }

            try
            {
                this.UpgradeVersionDescription.NeedToUpgrade = true;
                this.UpgradeVersionDescription.State = UpgradeState.Downloading;
                await this.DownloadInstaller().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Debug.Log($"Failed to download upgrade: {exception.Message}.");
                if (this.UpgradeVersionDescription != null)
                {
                    this.UpgradeVersionDescription.NeedToUpgrade = false;
                    this.UpgradeVersionDescription.State = UpgradeState.Failed;
                    this.UpgradeVersionDescription.InstallerDownloadInProgress = false;
                }
            }
        }

        public void CancelUpgrade()
        {
            if (this.UpgradeVersionDescription == null)
            {
                Debug.Log("Can't cancel upgrade because there is no upgrade in progress.");
                return;
            }

            Debug.Log("Cancel application upgrade.");
            this.UpgradeVersionDescription.NeedToUpgrade = false;
            this.UpgradeVersionDescription.State = UpgradeState.Available;
            try
            {
                this.webClient.CancelAsync();
            }
            catch (Exception)
            {
                // Ignore cancel failures.
            }

            this.CleanupCurrentDownload(deleteFiles: true);
        }

        /// <summary>
        /// Verifies installer integrity via SHA-256, WinVerifyTrust, chain policy and publisher identity.
        /// Deletes the package (and download directory) when verification fails.
        /// </summary>
        public static bool VerifyInstallerPackage(UpgradeVersionDescription description, out string failureReason)
        {
            failureReason = null;

            if (description == null)
            {
                failureReason = "Upgrade description is missing.";
                return false;
            }

            var request = new InstallerVerificationRequest
            {
                InstallerPath = description.InstallerPath,
                ExpectedSha256 = description.InstallerSHA256,
                ExpectedPublisher = description.ExpectedPublisher,
                AllowedCertificateThumbprints = ParseThumbprints(description.CertificateThumbprints),
                AllowOfflineRevocationUnknown = true
            };

            InstallerVerificationResult result = InstallerPackageVerifier.Verify(request);
            if (!result.Success)
            {
                failureReason = result.FailureReason;
                Debug.Log(failureReason);
                CleanupDownloadArtifacts(description);
                return false;
            }

            Debug.Log($"Installer verified. Subject: {result.CertificateSubject}; Thumbprint: {result.CertificateThumbprint}");
            return true;
        }

        private static IList<string> ParseThumbprints(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            return raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();
        }

        private static void CleanupDownloadArtifacts(UpgradeVersionDescription description)
        {
            if (description == null)
            {
                return;
            }

            string dir = description.DownloadDirectory;
            string path = description.InstallerPath;
            description.InstallerPath = null;

            if (!string.IsNullOrEmpty(dir))
            {
                InstallerPackageVerifier.TryDeletePath(dir);
            }
            else if (!string.IsNullOrEmpty(path))
            {
                InstallerPackageVerifier.TryDeletePath(path);
                try
                {
                    string parent = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(parent) &&
                        Path.GetFileName(parent).StartsWith("FileConverter-Upgrade-", StringComparison.OrdinalIgnoreCase))
                    {
                        InstallerPackageVerifier.TryDeletePath(parent);
                    }
                }
                catch
                {
                    // Ignore.
                }
            }
        }

        private void CleanupCurrentDownload(bool deleteFiles)
        {
            if (this.UpgradeVersionDescription == null)
            {
                return;
            }

            if (deleteFiles)
            {
                CleanupDownloadArtifacts(this.UpgradeVersionDescription);
            }

            this.UpgradeVersionDescription.DownloadDirectory = null;
            this.UpgradeVersionDescription.InstallerDownloadInProgress = false;
        }

        private async Task<UpgradeVersionDescription> DownloadLatestVersionDescription()
        {
#if BUILD32
            Uri uri = new Uri(Helpers.BaseURI + "version (x86).xml");
#else
            Uri uri = new Uri(UpgradeService.BaseURI + "version.xml");
#endif

            UpgradeVersionDescription description = null;
            try
            {
                Stream stream = await this.webClient.OpenReadTaskAsync(uri).ConfigureAwait(false);

                XmlRootAttribute xmlRoot = new XmlRootAttribute
                {
                    ElementName = "Version"
                };

                XmlSerializer serializer = new XmlSerializer(typeof(UpgradeVersionDescription), xmlRoot);

                XmlReaderSettings xmlReaderSettings = new XmlReaderSettings
                {
                    IgnoreWhitespace = true,
                    IgnoreComments = true,
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };

                using (XmlReader xmlReader = XmlReader.Create(stream, xmlReaderSettings))
                {
                    description = (UpgradeVersionDescription)serializer.Deserialize(xmlReader);
                }
            }
            catch (Exception)
            {
                Debug.Log("Error while retrieving version description.");
                return null;
            }

            return description;
        }

        private async Task DownloadInstaller()
        {
            if (this.UpgradeVersionDescription == null)
            {
                throw new ArgumentNullException(nameof(this.UpgradeVersionDescription));
            }

            if (this.UpgradeVersionDescription.InstallerDownloadInProgress)
            {
                throw new Exception("The installer download is currently in progress.");
            }

            Uri uri = new Uri(this.UpgradeVersionDescription.InstallerURL);

            string fileName = "FileConverter-setup.msi";
            Regex retrieveFileNameRegex = new Regex("/([^/?#]+)$");
            Match match = retrieveFileNameRegex.Match(this.UpgradeVersionDescription.InstallerURL);
            if (match.Success && match.Groups.Count > 1)
            {
                fileName = match.Groups[1].Value;
            }

            // Use an exclusive random directory under TEMP to avoid predictable paths and collisions.
            string downloadDirectory = Path.Combine(Path.GetTempPath(), "FileConverter-Upgrade-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(downloadDirectory);
            string installerPath = Path.Combine(downloadDirectory, fileName);

            // Restrict extension for the final path.
            if (!FileExtension.IsInstallerExtension(installerPath))
            {
                installerPath = Path.Combine(downloadDirectory, "FileConverter-setup.msi");
            }

            this.UpgradeVersionDescription.DownloadDirectory = downloadDirectory;
            this.UpgradeVersionDescription.InstallerPath = installerPath;
            this.UpgradeVersionDescription.InstallerDownloadInProgress = true;
            this.UpgradeVersionDescription.InstallerDownloadProgress = 0;
            this.UpgradeVersionDescription.State = UpgradeState.Downloading;

            // Source: https://stackoverflow.com/questions/2859790/the-request-was-aborted-could-not-create-ssl-tls-secure-channel#2904963
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            this.webClient.DownloadProgressChanged += this.WebClient_DownloadProgressChanged;

            try
            {
                await this.webClient.DownloadFileTaskAsync(uri, installerPath).ConfigureAwait(false);

                // Keep UpgradeVersionDescription so OnExit can launch the installer.
                this.UpgradeVersionDescription.InstallerDownloadProgress = 100;
                this.UpgradeVersionDescription.InstallerDownloadInProgress = false;
                this.UpgradeVersionDescription.NeedToUpgrade = true;
                this.UpgradeVersionDescription.State = UpgradeState.ReadyToInstall;

                if (!VerifyInstallerPackage(this.UpgradeVersionDescription, out string verifyError))
                {
                    Debug.LogError($"Installer verification failed: {verifyError}");
                    this.UpgradeVersionDescription.NeedToUpgrade = false;
                    this.UpgradeVersionDescription.State = UpgradeState.Failed;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("Failed to download the new File Converter upgrade. You should try again or download it manually.");
                Debug.Log(exception.ToString());
                if (this.UpgradeVersionDescription != null)
                {
                    this.UpgradeVersionDescription.NeedToUpgrade = false;
                    this.UpgradeVersionDescription.State = UpgradeState.Failed;
                    CleanupDownloadArtifacts(this.UpgradeVersionDescription);
                }
            }
            finally
            {
                this.webClient.DownloadProgressChanged -= this.WebClient_DownloadProgressChanged;
                if (this.UpgradeVersionDescription != null)
                {
                    this.UpgradeVersionDescription.InstallerDownloadInProgress = false;
                }
            }
        }
        
        private void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs eventArgs)
        {
            if (this.UpgradeVersionDescription != null)
            {
                this.UpgradeVersionDescription.InstallerDownloadProgress = eventArgs.ProgressPercentage;
            }
        }
    }
}
