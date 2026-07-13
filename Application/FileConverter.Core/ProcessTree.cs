// <copyright file="ProcessTree.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Core
{
    using System;
    using System.Diagnostics;

    /// <summary>
    /// Terminates a process and its descendants on Windows.
    /// </summary>
    public static class ProcessTree
    {
        public static void Kill(Process process, int waitMilliseconds = 5000)
        {
            if (process == null)
            {
                return;
            }

            try
            {
                if (process.HasExited)
                {
                    return;
                }
            }
            catch (InvalidOperationException)
            {
                return;
            }

            int pid;
            try
            {
                pid = process.Id;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            // Prefer taskkill tree kill; fall back to Process.Kill.
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/PID {pid} /T /F",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (Process killer = Process.Start(psi))
                {
                    killer?.WaitForExit(waitMilliseconds);
                }
            }
            catch
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
            }

            try
            {
                process.WaitForExit(waitMilliseconds);
            }
            catch
            {
                // Ignore.
            }
        }
    }
}
