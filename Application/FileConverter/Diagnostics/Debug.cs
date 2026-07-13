// <copyright file="Debug.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Diagnostics
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Windows;

    public static class Debug
    {
        private static readonly string diagnosticsFolderPath;
        private static readonly Dictionary<int, DiagnosticsData> diagnosticsDataById = new Dictionary<int, DiagnosticsData>();
        private static int threadCount = 0;
        private static readonly int mainThreadId = 0;

        private const int DiagnosticsRetentionDays = 1;
        private const long DiagnosticsMaxTotalBytes = 100L * 1024 * 1024; // 100 MB soft cap

        static Debug()
        {
            Debug.mainThreadId = Thread.CurrentThread.ManagedThreadId;

            string path = FileConverterExtension.PathHelpers.GetUserDataFolderPath;

            // Delete old diagnostics folders; never let cleanup failures block type initialization.
            try
            {
                CleanupOldDiagnostics(path);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Diagnostics cleanup failed: {exception.Message}");
            }

            string diagnosticsFolderName = $"Diagnostics-{DateTime.Now.Hour}h{DateTime.Now.Minute}m{DateTime.Now.Second}s";
            
            Debug.diagnosticsFolderPath = Path.Combine(path, diagnosticsFolderName);
            Debug.diagnosticsFolderPath = PathHelpers.GenerateUniquePath(Debug.diagnosticsFolderPath);
            Directory.CreateDirectory(Debug.diagnosticsFolderPath);

            Debug.Log($"Diagnostics stored at path '{Debug.diagnosticsFolderPath}'");
        }

        private static void CleanupOldDiagnostics(string userDataPath)
        {
            if (!Directory.Exists(userDataPath))
            {
                return;
            }

            DateTime expirationDate = DateTime.Now.Subtract(TimeSpan.FromDays(DiagnosticsRetentionDays));
            string[] diagnosticsDirectories;
            try
            {
                diagnosticsDirectories = Directory.GetDirectories(userDataPath, "Diagnostics-*");
            }
            catch
            {
                return;
            }

            long totalSize = 0;
            var dirInfos = new List<Tuple<string, DateTime, long>>();

            for (int index = 0; index < diagnosticsDirectories.Length; index++)
            {
                string directory = diagnosticsDirectories[index];
                try
                {
                    DateTime creationTime = Directory.GetCreationTime(directory);
                    long size = GetDirectorySizeSafe(directory);
                    totalSize += size;
                    dirInfos.Add(Tuple.Create(directory, creationTime, size));

                    if (creationTime < expirationDate)
                    {
                        try
                        {
                            Directory.Delete(directory, true);
                            totalSize -= size;
                        }
                        catch
                        {
                            // Permission or file lock — skip this folder.
                        }
                    }
                }
                catch
                {
                    // Skip directories we cannot inspect.
                }
            }

            // Enforce soft total size cap by deleting oldest remaining folders first.
            if (totalSize > DiagnosticsMaxTotalBytes)
            {
                foreach (Tuple<string, DateTime, long> info in dirInfos.OrderBy(d => d.Item2))
                {
                    if (totalSize <= DiagnosticsMaxTotalBytes)
                    {
                        break;
                    }

                    if (!Directory.Exists(info.Item1))
                    {
                        continue;
                    }

                    try
                    {
                        Directory.Delete(info.Item1, true);
                        totalSize -= info.Item3;
                    }
                    catch
                    {
                        // Ignore.
                    }
                }
            }
        }

        private static long GetDirectorySizeSafe(string directory)
        {
            long size = 0;
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        size += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // Ignore individual file failures.
                    }
                }
            }
            catch
            {
                // Ignore.
            }

            return size;
        }

        /// <summary>
        /// Redacts common user-path prefixes from diagnostic text for safer export.
        /// </summary>
        public static string SanitizeForExport(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return message;
            }

            try
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userProfile))
                {
                    message = message.Replace(userProfile, "%USERPROFILE%");
                }

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(localAppData))
                {
                    message = message.Replace(localAppData, "%LOCALAPPDATA%");
                }
            }
            catch
            {
                // Best-effort sanitization.
            }

            return message;
        }

        public static int FirstErrorCode
        {
            get;
            private set;
        }

        public static event EventHandler<PropertyChangedEventArgs> StaticPropertyChanged;

        public static DiagnosticsData[] Data => Debug.diagnosticsDataById.Values.ToArray();

        public static void Log(string message)
        {
            Debug.LogInternal(error: false, message, ConsoleColor.White);
        }

        public static void Assert(bool condition)
        {
            if (!condition)
            {
                LogError("Assertion failed");
            }
        }

        public static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                LogError(message);
            }
        }

        public static void LogError(string message)
        {
            // Only show UI on the main thread; background threads log only.
            try
            {
                if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        catch
                        {
                            // Ignore UI failures during shutdown.
                        }
                    }));
                }
                else
                {
                    MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch
            {
                // Headless / early-init paths may not have a message pump.
            }

            Debug.LogInternal(error: true, $"Error: {message}", ConsoleColor.Red);
        }

        public static void LogError(int errorCode, string message)
        {
            if (Debug.FirstErrorCode == 0)
            {
                Debug.FirstErrorCode = errorCode;
            }

            Debug.LogError($"{message} (code 0x{errorCode:X})");
        }

        public static void Release()
        {
            Debug.Log("Diagnostics manager released correctly.");

            foreach (KeyValuePair<int, DiagnosticsData> kvp in Debug.diagnosticsDataById)
            {
                kvp.Value.Release();
            }

            Debug.diagnosticsDataById.Clear();
        }

        private static void LogInternal(bool error, string log, ConsoleColor color)
        {
            DiagnosticsData diagnosticsData;

            Thread currentThread = Thread.CurrentThread;
            int threadId = currentThread.ManagedThreadId;

            // Display main thread logs in standard output.
            if (threadId == Debug.mainThreadId)
            {
                Console.ForegroundColor = color;
                if (error)
                {
                    Console.Error.WriteLine(log);
                }
                else
                {
                    Console.WriteLine(log);
                }

                Console.ResetColor();
            }

            lock (Debug.diagnosticsDataById)
            {
                if (!Debug.diagnosticsDataById.TryGetValue(threadId, out diagnosticsData))
                {
                    string threadName = Debug.threadCount > 0 ? $"{currentThread.Name} ({Debug.threadCount})" : "Application";
                    diagnosticsData = new DiagnosticsData(threadName);
                    diagnosticsData.Initialize(Debug.diagnosticsFolderPath, threadId);
                    Debug.diagnosticsDataById.Add(threadId, diagnosticsData);
                    Debug.threadCount++;

                    StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs("Data"));
                }
            }

            diagnosticsData.Log(SanitizeForExport(log));
        }
    }
}
