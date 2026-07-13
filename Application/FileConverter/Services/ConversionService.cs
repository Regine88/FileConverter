// <copyright file="ConversionService.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.Services
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Threading;
    using System.Threading.Tasks;

    using CommunityToolkit.Mvvm.ComponentModel;

    using FileConverter.ConversionJobs;
    using FileConverter.Diagnostics;

    public class ConversionService : ObservableObject, IConversionService
    {
        private readonly List<ConversionJob> conversionJobs = new List<ConversionJob>();
        private readonly int numberOfConversionThread = 1;
        private readonly ISettingsService settingsService;
        private readonly object schedulerLock = new object();
        private int runningJobCount;
        private bool cdExtractionInProgress;

        public ConversionService(ISettingsService settingsService)
        {
            if (settingsService == null)
            {
                throw new ArgumentNullException(nameof(settingsService));
            }

            this.settingsService = settingsService;

            this.ConversionJobs = this.conversionJobs.AsReadOnly();

            this.numberOfConversionThread = this.settingsService.Settings.MaximumNumberOfSimultaneousConversions;
            Debug.Log($"Maximum number of conversion threads: {this.numberOfConversionThread}");

            if (this.numberOfConversionThread <= 0)
            {
                this.numberOfConversionThread = System.Math.Max(1, Environment.ProcessorCount / 2);
                Debug.Log($"The number of processors on this computer is {Environment.ProcessorCount}. Set the default number of conversion threads to {this.numberOfConversionThread}");
            }
        }

        public event System.EventHandler<ConversionJobsTerminatedEventArgs> ConversionJobsTerminated;

        public ReadOnlyCollection<ConversionJob> ConversionJobs
        {
            get;
            private set;
        }

        public void RegisterConversionJob(ConversionJob conversionJob)
        {
            this.conversionJobs.Add(conversionJob);
            this.OnPropertyChanged(nameof(this.ConversionJobs));
        }

        public void ConvertFilesAsync()
        {
            // Scheduler may touch Office during PrepareConversion (page counts); keep it on STA.
            Thread schedulerThread = Helpers.InstantiateThread("ConversionQueueThread", () =>
            {
                try
                {
                    this.ConvertFilesCore();
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Conversion queue failed: {exception}");
                }
            });
            schedulerThread.SetApartmentState(ApartmentState.STA);
            schedulerThread.Start();
        }

        private void ConvertFilesCore()
        {
            // Prepare conversions (may open Office for page counts — must be STA).
            for (int index = 0; index < this.ConversionJobs.Count; index++)
            {
                try
                {
                    this.ConversionJobs[index].PrepareConversion();
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Failed to prepare conversion job: {exception}");
                    try
                    {
                        // Best-effort: mark failed if still unknown/ready.
                        if (this.ConversionJobs[index].State != ConversionState.Failed)
                        {
                            Debug.Log($"Job state after prepare failure: {this.ConversionJobs[index].State}");
                        }
                    }
                    catch
                    {
                        // Ignore.
                    }
                }
            }

            System.Collections.Specialized.StringCollection files = new System.Collections.Specialized.StringCollection();
            using (SemaphoreSlim concurrency = new SemaphoreSlim(this.numberOfConversionThread, this.numberOfConversionThread))
            {
                var running = new List<Task>();

                for (int jobIndex = 0; jobIndex < this.conversionJobs.Count; jobIndex++)
                {
                    ConversionJob conversionJob = this.conversionJobs[jobIndex];
                    if (conversionJob.State != ConversionState.Ready)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(conversionJob.OutputFilePath) && !files.Contains(conversionJob.OutputFilePath))
                    {
                        files.Add(conversionJob.OutputFilePath);
                    }

                    bool needsCdLock = (conversionJob.StateFlags & ConversionFlags.CdDriveExtraction) != 0;

                    // Match original CanStartConversion semantics for CD extraction exclusivity.
                    while (true)
                    {
                        bool canStart;
                        lock (this.schedulerLock)
                        {
                            if (needsCdLock)
                            {
                                canStart = !this.cdExtractionInProgress && this.runningJobCount == 0;
                                if (canStart)
                                {
                                    this.cdExtractionInProgress = true;
                                    this.runningJobCount++;
                                }
                            }
                            else
                            {
                                canStart = !this.cdExtractionInProgress && this.runningJobCount < this.numberOfConversionThread;
                                if (canStart)
                                {
                                    this.runningJobCount++;
                                }
                            }
                        }

                        if (canStart)
                        {
                            break;
                        }

                        Thread.Sleep(20);
                    }

                    concurrency.Wait();

                    // Office COM (Word/Excel/PowerPoint) requires STA. FFmpeg/ImageMagick are fine on STA too.
                    // Task.Run uses MTA pool threads and can hard-crash the process during Word interop.
                    TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
                    Thread jobThread = Helpers.InstantiateThread(
                        conversionJob.GetType().Name,
                        () =>
                        {
                            try
                            {
                                this.ExecuteConversionJob(conversionJob);
                                tcs.TrySetResult(true);
                            }
                            catch (Exception exception)
                            {
                                Debug.LogError($"Conversion thread faulted: {exception}");
                                tcs.TrySetException(exception);
                            }
                            finally
                            {
                                lock (this.schedulerLock)
                                {
                                    this.runningJobCount = Math.Max(0, this.runningJobCount - 1);
                                    if (needsCdLock)
                                    {
                                        this.cdExtractionInProgress = false;
                                    }
                                }

                                concurrency.Release();
                            }
                        });
                    jobThread.SetApartmentState(ApartmentState.STA);
                    jobThread.IsBackground = true;
                    jobThread.Start();
                    running.Add(tcs.Task);
                }

                if (running.Count > 0)
                {
                    try
                    {
                        Task.WaitAll(running.ToArray());
                    }
                    catch (AggregateException aggregateException)
                    {
                        foreach (Exception inner in aggregateException.Flatten().InnerExceptions)
                        {
                            Debug.LogError($"Conversion task error: {inner}");
                        }
                    }
                }
            }

            // Copy the output files to the clipboard
            if (this.settingsService.Settings.CopyFilesInClipboardAfterConversion && files.Count > 0)
            {
                Thread clipboardThread = Helpers.InstantiateThread("CopyFilesToClipboardThread", this.CopyFilesToClipboard);
                clipboardThread.SetApartmentState(ApartmentState.STA);
                clipboardThread.Start(files);
                clipboardThread.Join(TimeSpan.FromSeconds(10));
            }

            bool allConversionsSucceed = true;
            for (int index = 0; index < this.conversionJobs.Count; index++)
            {
                allConversionsSucceed &= this.conversionJobs[index].State == ConversionState.Done;
            }

            this.ConversionJobsTerminated?.Invoke(this, new ConversionJobsTerminatedEventArgs(allConversionsSucceed));
        }

        private void ExecuteConversionJob(ConversionJob conversionJob)
        {
            if (conversionJob == null)
            {
                throw new System.ArgumentException("The parameter must be a conversion job.", nameof(conversionJob));
            }

            if (conversionJob.State != ConversionState.Ready)
            {
                Debug.LogError("Fail to execute conversion job.");
                return;
            }

            try
            {
                conversionJob.StartConversion();
            }
            catch (Exception exception)
            {
                Debug.LogError($"Failure during conversion: {exception}");
            }
        }

        private void CopyFilesToClipboard(object _filePaths)
        {
            try
            {
                System.Collections.Specialized.StringCollection FilePaths = _filePaths as System.Collections.Specialized.StringCollection;
                System.Windows.Forms.Clipboard.SetFileDropList(FilePaths);
                Debug.Log("Output files copied to the clipboard:");
                for (int index = 0; index < FilePaths.Count; index++)
                {
                    Debug.Log($"  {FilePaths[index]}");
                }
            }
            catch (Exception exception)
            {
                Debug.Log("Can't copy files to the clipboard.");
                Debug.Log($"An exception has been thrown: {exception}.");
            }
        }
    }
}
