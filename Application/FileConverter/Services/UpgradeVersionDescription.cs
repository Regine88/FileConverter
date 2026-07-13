// <copyright file="UpgradeVersionDescription.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Services
{
    using System.Xml.Serialization;

    using CommunityToolkit.Mvvm.ComponentModel;

    /// <summary>
    /// High-level upgrade lifecycle. Replaces ad-hoc booleans that could contradict each other.
    /// </summary>
    public enum UpgradeState
    {
        Idle,
        Checking,
        Available,
        Downloading,
        ReadyToInstall,
        Installing,
        Failed,
    }

    public class UpgradeVersionDescription : ObservableObject
    {
        private int installerDownloadProgress;
        private bool installerDownloadInProgress;
        private UpgradeState state = UpgradeState.Idle;
        private string changeLog;

        [XmlElement("Latest")]
        public Version LatestVersion
        {
            get;
            set;
        }
        
        [XmlElement("URL")]
        public string InstallerURL
        {
            get;
            set;
        }

        /// <summary>
        /// Required SHA-256 (hex) of the installer payload. Must match before install.
        /// </summary>
        [XmlElement("SHA256")]
        public string InstallerSHA256
        {
            get;
            set;
        }

        /// <summary>
        /// Required Authenticode subject substring (e.g. CN=Publisher Name).
        /// </summary>
        [XmlElement("Publisher")]
        public string ExpectedPublisher
        {
            get;
            set;
        }

        /// <summary>
        /// Optional comma-separated certificate SHA-1 thumbprints for rotation (no spaces or with spaces).
        /// </summary>
        [XmlElement("CertificateThumbprints")]
        public string CertificateThumbprints
        {
            get;
            set;
        }

        [XmlIgnore]
        public string ChangeLog
        {
            get => this.changeLog;
            set
            {
                this.changeLog = value;
                this.OnPropertyChanged();
            }
        }

        [XmlIgnore]
        public string InstallerPath
        {
            get;
            set;
        }

        /// <summary>
        /// Directory that owns the downloaded installer; cleaned up on failure/cancel/stale startup.
        /// </summary>
        [XmlIgnore]
        public string DownloadDirectory
        {
            get;
            set;
        }

        [XmlIgnore]
        public UpgradeState State
        {
            get => this.state;
            set
            {
                this.state = value;
                this.OnPropertyChanged();
            }
        }

        [XmlIgnore]
        public bool InstallerDownloadInProgress
        {
            get => this.installerDownloadInProgress;

            set
            {
                this.installerDownloadInProgress = value;
                this.OnPropertyChanged();
                this.OnPropertyChanged(nameof(this.InstallerDownloadDone));
                this.OnPropertyChanged(nameof(this.InstallerDownloadNotStarted));
            }
        }

        [XmlIgnore]
        public int InstallerDownloadProgress
        {
            get => this.installerDownloadProgress;

            set
            {
                this.installerDownloadProgress = value;
                this.OnPropertyChanged();
                this.OnPropertyChanged(nameof(this.InstallerDownloadDone));
                this.OnPropertyChanged(nameof(this.InstallerDownloadNotStarted));
            }
        }

        [XmlIgnore]
        public bool InstallerDownloadDone => !this.InstallerDownloadInProgress && this.InstallerDownloadProgress == 100;

        [XmlIgnore]
        public bool InstallerDownloadNotStarted => !this.InstallerDownloadInProgress && this.InstallerDownloadProgress == 0;

        [XmlIgnore]
        public bool NeedToUpgrade
        {
            get;
            set;
        }
    }
}
