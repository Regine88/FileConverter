// <copyright file="SettingsService.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

using FileConverter.Properties;

namespace FileConverter.Services
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Windows;

    using CommunityToolkit.Mvvm.ComponentModel;

    using Debug = FileConverter.Diagnostics.Debug;

    public partial class SettingsService : ObservableObject, ISettingsService
    {
        private static readonly object SaveLock = new object();

        public SettingsService()
        {
            // Load settigns.
            Debug.Log("Load settings...");
            this.Settings = this.Load();
        }

        public Settings Settings
        {
            get;
            private set;
        }

        public bool PostInstallationInitialization()
        {
            Debug.Log("Execute post installation initialization.");

            Settings defaultSettings = null;

            // Load the default settings.
            if (File.Exists(FileConverterExtension.PathHelpers.DefaultSettingsFilePath))
            {
                try
                {
                    XmlHelpers.LoadFromFile<Settings>("Settings", FileConverterExtension.PathHelpers.DefaultSettingsFilePath, out defaultSettings);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Fail to load file converter default settings. {exception.Message}");
                    return false;
                }
            }
            else
            {
                Debug.LogError($"Default settings not found at path {FileConverterExtension.PathHelpers.DefaultSettingsFilePath}. You should try to reinstall the application.");
                return false;
            }

            // Load user settings if exists.
            Settings userSettings = null;
            if (File.Exists(FileConverterExtension.PathHelpers.UserSettingsFilePath))
            {
                try
                {
                    XmlHelpers.LoadFromFile<Settings>("Settings", FileConverterExtension.PathHelpers.UserSettingsFilePath, out userSettings);
                }
                catch (Exception)
                {
                    this.PreserveCorruptSettingsFile(FileConverterExtension.PathHelpers.UserSettingsFilePath);
                }

                if (userSettings != null)
                {
                    if (userSettings.SerializationVersion != Settings.Version)
                    {
                        this.MigrateSettingsToCurrentVersion(userSettings);

                        Debug.Log($"File converter settings have been imported from version {userSettings.SerializationVersion} to version {Settings.Version}.");
                        userSettings.SerializationVersion = Settings.Version;
                    }

                    // Remove default settings.
                    if (userSettings.ConversionPresets != null)
                    {
                        for (int index = userSettings.ConversionPresets.Count - 1; index >= 0; index--)
                        {
                            if (userSettings.ConversionPresets[index].IsDefaultSettings)
                            {
                                userSettings.ConversionPresets.RemoveAt(index);
                            }
                        }
                    }
                }
            }

            Settings settings = userSettings != null ? userSettings.Merge(defaultSettings) : defaultSettings;
            return this.Save(settings);
        }

        public void SaveSettings()
        {
            this.Save(this.Settings);
        }

        public void RevertSettings()
        {
            // Load previous preset in order to cancel changes.
            this.Settings = this.Load();
        }

        private Settings Load()
        {
            Settings settings = null;
            if (File.Exists(FileConverterExtension.PathHelpers.UserSettingsFilePath))
            {
                Settings userSettings = null;
                try
                {
                    var stopwatch = new Stopwatch();
                    stopwatch.Start();
                    XmlHelpers.LoadFromFile<Settings>("Settings", FileConverterExtension.PathHelpers.UserSettingsFilePath, out userSettings);
                    stopwatch.Stop();
                    Debug.Log($"Settings load time: {stopwatch.Elapsed.TotalMilliseconds}ms");

                    settings = userSettings;
                }
                catch (Exception)
                {
                    MessageBoxResult messageBoxResult =
                        MessageBox.Show(Resources.ErrorCantLoadSettings,
                            Resources.Error,
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Exclamation);

                    if (messageBoxResult == MessageBoxResult.Yes)
                    {
                        // Keep a timestamped corrupt copy for troubleshooting instead of hard-deleting.
                        this.PreserveCorruptSettingsFile(FileConverterExtension.PathHelpers.UserSettingsFilePath);
                        return this.Load();
                    }
                    else if (messageBoxResult == MessageBoxResult.No)
                    {
                        return null;
                    }
                }

                if (userSettings != null && userSettings.SerializationVersion != Settings.Version)
                {
                    this.MigrateSettingsToCurrentVersion(userSettings);

                    Debug.Log($"File converter settings has been imported from version {userSettings.SerializationVersion} to version {Settings.Version}.");
                    userSettings.SerializationVersion = Settings.Version;
                    this.Save(userSettings);
                }
            }
            else
            {
                // Load the default settings.
                if (File.Exists(FileConverterExtension.PathHelpers.DefaultSettingsFilePath))
                {
                    try
                    {
                        XmlHelpers.LoadFromFile<Settings>("Settings", FileConverterExtension.PathHelpers.DefaultSettingsFilePath, out Settings defaultSettings);
                        settings = defaultSettings;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError($"Fail to load file converter default settings. {exception.Message}");
                    }
                }
                else
                {
                    Debug.LogError($"Default settings not found at path {FileConverterExtension.PathHelpers.DefaultSettingsFilePath}. You should try to reinstall the application.");
                }
            }

            return settings;
        }

        private bool Save(Settings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Clean();

            string settingsPath = FileConverterExtension.PathHelpers.UserSettingsFilePath;
            string directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            lock (SaveLock)
            {
                // Write to a unique temp file in the same directory, then replace atomically with a .bak backup.
                string tempPath = Path.Combine(directory ?? Path.GetTempPath(), $"Settings.{Guid.NewGuid():N}.tmp.xml");
                string backupPath = settingsPath + ".bak";

                try
                {
                    XmlHelpers.SaveToFile("Settings", tempPath, settings);

                    // Ensure data is flushed to disk before replace.
                    using (FileStream flushStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        flushStream.Flush(true);
                    }

                    if (File.Exists(settingsPath))
                    {
                        // Atomic replace when possible; keeps previous content as .bak.
                        File.Replace(tempPath, settingsPath, backupPath, ignoreMetadataErrors: true);
                    }
                    else
                    {
                        File.Move(tempPath, settingsPath);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Failed to save settings atomically: {exception.Message}");
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch
                    {
                        // Ignore cleanup errors.
                    }

                    return false;
                }
            }

            return true;
        }

        private void PreserveCorruptSettingsFile(string settingsPath)
        {
            try
            {
                if (!File.Exists(settingsPath))
                {
                    return;
                }

                string directory = Path.GetDirectoryName(settingsPath) ?? string.Empty;
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string corruptPath = Path.Combine(directory, $"Settings.corrupt.{stamp}.xml");
                if (File.Exists(corruptPath))
                {
                    corruptPath = Path.Combine(directory, $"Settings.corrupt.{stamp}.{Guid.NewGuid():N}.xml");
                }

                File.Move(settingsPath, corruptPath);
                Debug.Log($"Corrupt settings preserved at: {corruptPath}");
            }
            catch (Exception exception)
            {
                Debug.Log($"Failed to preserve corrupt settings file: {exception.Message}");
                try
                {
                    File.Delete(settingsPath);
                }
                catch
                {
                    // Last resort — ignore.
                }
            }
        }
    }
}
