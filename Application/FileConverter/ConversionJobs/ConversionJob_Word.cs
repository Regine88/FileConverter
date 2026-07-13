// <copyright file="ConversionJob_Word.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ConversionJobs
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;

    using FileConverter.Diagnostics;

    using Word = NetOffice.WordApi;

    public class ConversionJob_Word : ConversionJob_Office
    {
        private Word.Document document;
        private Word.Application application;

        private string intermediateFilePath = string.Empty;
        private ConversionJob pdf2ImageConversionJob = null;

        public ConversionJob_Word() : base()
        {
        }

        public ConversionJob_Word(ConversionPreset conversionPreset, string inputFilePath) : base(conversionPreset, inputFilePath)
        {
        }

        protected override ApplicationName Application => ApplicationName.Word;

        protected override bool IsCancelable() => false;

        protected override int GetOutputFilesCount()
        {
            if (this.ConversionPreset.OutputType == OutputType.Pdf)
            {
                return 1;
            }

            if (!this.TryLoadDocumentIfNecessary())
            {
                return 1;
            }

            try
            {
                int pagesCount = this.document.ComputeStatistics(Word.Enums.WdStatistic.wdStatisticPages);
                return Math.Max(1, pagesCount);
            }
            catch (Exception exception)
            {
                Debug.Log($"Failed to compute Word page count: {exception.Message}");
                return 1;
            }
        }

        protected override void Initialize()
        {
            base.Initialize();

            if (this.State == ConversionState.Failed)
            {
                return;
            }

            if (this.ConversionPreset == null)
            {
                throw new System.Exception("The conversion preset must be valid.");
            }

            // Initialize converters.
            if (this.ConversionPreset.OutputType == OutputType.Pdf)
            {
                this.intermediateFilePath = this.OutputFilePath;
            }
            else
            {
                // Generate intermediate file path (ASCII-safe under TEMP to avoid COM path issues).
                string fileName = Path.GetFileNameWithoutExtension(this.InputFilePath);
                string safeName = string.IsNullOrEmpty(fileName) ? "word-doc" : fileName;
                string tempPath = Path.GetTempPath();
                this.intermediateFilePath = PathHelpers.GenerateUniquePath(Path.Combine(tempPath, safeName + ".pdf"));

                ConversionPreset intermediatePreset = new ConversionPreset("Pdf to image", this.ConversionPreset, "pdf");
                this.pdf2ImageConversionJob = ConversionJobFactory.Create(intermediatePreset, this.intermediateFilePath);
                this.pdf2ImageConversionJob.PrepareConversion(this.OutputFilePaths);
            }
        }

        protected override void Convert()
        {
            if (this.ConversionPreset == null)
            {
                throw new System.Exception("The conversion preset must be valid.");
            }

            this.UserState = Properties.Resources.ConversionStateReadDocument;

            try
            {
                if (!this.TryLoadDocumentIfNecessary())
                {
                    this.ConversionFailed(Properties.Resources.ErrorUnableToUseMicrosoftOffice);
                    return;
                }

                // Make this document the active document.
                this.document.Activate();

                this.UserState = Properties.Resources.ConversionStateConversion;

                Debug.Log("Convert word document to pdf.");
                Debug.Log($"Export path: {this.intermediateFilePath}");

                // Ensure parent directory exists for export.
                string exportDir = Path.GetDirectoryName(this.intermediateFilePath);
                if (!string.IsNullOrEmpty(exportDir) && !Directory.Exists(exportDir))
                {
                    Directory.CreateDirectory(exportDir);
                }

                this.document.ExportAsFixedFormat(
                    this.intermediateFilePath,
                    Word.Enums.WdExportFormat.wdExportFormatPDF,
                    false,
                    Word.Enums.WdExportOptimizeFor.wdExportOptimizeForPrint,
                    Word.Enums.WdExportRange.wdExportAllDocument,
                    1,
                    1,
                    Word.Enums.WdExportItem.wdExportDocumentContent,
                    true,
                    true,
                    Word.Enums.WdExportCreateBookmarks.wdExportCreateHeadingBookmarks,
                    true);
            }
            catch (COMException comException)
            {
                Debug.Log(comException.ToString());
                this.ConversionFailed($"Microsoft Word COM error: {comException.Message}");
                return;
            }
            catch (Exception exception)
            {
                Debug.Log(exception.ToString());
                this.ConversionFailed(exception.Message);
                return;
            }
            finally
            {
                this.CloseDocumentSafe();
                this.ReleaseOfficeApplicationInstanceIfNeeded();
            }

            if (this.State == ConversionState.Failed)
            {
                return;
            }

            if (this.pdf2ImageConversionJob != null)
            {
                if (!System.IO.File.Exists(this.intermediateFilePath))
                {
                    this.ConversionFailed(Properties.Resources.ErrorCantFindOutputFiles);
                    return;
                }

                Task updateProgress = this.UpdateProgress();

                Debug.Log("Convert pdf to images.");

                this.pdf2ImageConversionJob.StartConversion();

                if (this.pdf2ImageConversionJob.State != ConversionState.Done)
                {
                    this.ConversionFailed(this.pdf2ImageConversionJob.ErrorMessage);
                    return;
                }

                if (!string.IsNullOrEmpty(this.intermediateFilePath))
                {
                    Debug.Log($"Delete intermediate file {this.intermediateFilePath}.");

                    try
                    {
                        File.Delete(this.intermediateFilePath);
                    }
                    catch (Exception exception)
                    {
                        Debug.Log($"Failed to delete intermediate PDF: {exception.Message}");
                    }
                }

                updateProgress.Wait();
            }
        }

        protected override void InitializeOfficeApplicationInstanceIfNecessary()
        {
            if (this.application != null)
            {
                return;
            }

            // Initialize word application.
            Debug.Log("Instantiate word application via interop.");
            Debug.Log($"Thread apartment state: {System.Threading.Thread.CurrentThread.GetApartmentState()}");

            this.application = new Word.Application
            {
                Visible = false,
                DisplayAlerts = Word.Enums.WdAlertLevel.wdAlertsNone
            };
        }

        protected override void ReleaseOfficeApplicationInstanceIfNeeded()
        {
            if (this.application == null)
            {
                return;
            }

            try
            {
                Diagnostics.Debug.Log("Quit word application via interop.");
                this.application.Quit(Word.Enums.WdSaveOptions.wdDoNotSaveChanges);
            }
            catch (Exception exception)
            {
                Debug.Log($"Word Quit failed: {exception.Message}");
            }
            finally
            {
                try
                {
                    this.application.Dispose();
                }
                catch
                {
                    // NetOffice dispose best-effort.
                }

                this.application = null;
            }
        }

        private void CloseDocumentSafe()
        {
            if (this.document == null)
            {
                return;
            }

            try
            {
                Debug.Log($"Close word document '{this.InputFilePath}'.");
                this.document.Close(Word.Enums.WdSaveOptions.wdDoNotSaveChanges);
            }
            catch (Exception exception)
            {
                Debug.Log($"Word document Close failed: {exception.Message}");
            }
            finally
            {
                try
                {
                    this.document.Dispose();
                }
                catch
                {
                    // Ignore.
                }

                this.document = null;
            }
        }

        private async Task UpdateProgress()
        {
            while (this.pdf2ImageConversionJob.State != ConversionState.Done &&
                   this.pdf2ImageConversionJob.State != ConversionState.Failed)
            {
                if (this.pdf2ImageConversionJob != null && this.pdf2ImageConversionJob.State == ConversionState.InProgress)
                {
                    this.Progress = this.pdf2ImageConversionJob.Progress;
                }

                if (this.pdf2ImageConversionJob != null && this.pdf2ImageConversionJob.State == ConversionState.InProgress)
                {
                    this.Progress = this.pdf2ImageConversionJob.Progress;
                    this.UserState = this.pdf2ImageConversionJob.UserState;
                }

                await Task.Delay(40);
            }
        }

        private bool TryLoadDocumentIfNecessary()
        {
            try
            {
                this.InitializeOfficeApplicationInstanceIfNecessary();
            }
            catch (Exception exception)
            {
                Debug.Log(exception.ToString());
                Debug.Log("Failed to initialize office application.");
                return false;
            }

            if (this.application == null)
            {
                return false;
            }

            if (this.document == null)
            {
                try
                {
                    Debug.Log($"Load word document '{this.InputFilePath}'.");

                    if (!File.Exists(this.InputFilePath))
                    {
                        Debug.Log("Input file does not exist.");
                        return false;
                    }

                    // ReadOnly=true reduces lock conflicts with Explorer previews.
                    // NetOffice Open: FileName, ConfirmConversions, ReadOnly, AddToRecentFiles, ...
                    this.document = this.application.Documents.Open(
                        this.InputFilePath,
                        false,
                        true,
                        false);
                }
                catch (Exception exception)
                {
                    Debug.Log($"Failed to open Word document: {exception}");
                    return false;
                }
            }

            return this.document != null;
        }
    }
}
