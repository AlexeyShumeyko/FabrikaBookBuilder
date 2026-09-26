using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using PhotoBookRenamer.Application;
using PhotoBookRenamer.Domain;

namespace PhotoBookRenamer.Presentation.Dialogs
{
    /// <summary>
    /// Export options modal.
    ///
    /// Replaces the old FolderNameDialog-then-export-immediately flow: the user picks the
    /// target once, sees what will be produced, and the copy runs behind a progress bar
    /// instead of blocking the UI.
    ///
    /// The naming format is shown read-only on purpose. The client's site only accepts
    /// KKK-FF.jpg, so the alternative format from the design mockup was deliberately not
    /// implemented (see doc/AI_NOTES.md).
    /// </summary>
    public partial class ExportDialog : Window, INotifyPropertyChanged
    {
        private readonly Project _project;
        private readonly IExportService _exportService;

        private string _targetFolder;
        private double _progress;
        private bool _isRunning;
        private bool _exportSucceeded;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Raises PropertyChanged for the given property.
        ///
        /// The name must be real: <c>new PropertyChangedEventArgs(null)</c> throws
        /// ArgumentNullException, and because that happens inside a WPF binding callback it
        /// surfaces as STATUS_FATAL_USER_CALLBACK_EXCEPTION (0xC000041D) and kills the
        /// process with no managed stack trace.
        /// </summary>
        private void Raise(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>Folder the export was written to. Only valid after <see cref="Exported"/>.</summary>
        public string ExportedFolder { get; private set; } = string.Empty;

        /// <summary>True when the export ran and reported success.</summary>
        public bool Exported => _exportSucceeded;

        public ExportDialog(Project project, IExportService exportService, string? defaultFolder = null)
        {
            InitializeComponent();

            _project = project;
            _exportService = exportService;
            _targetFolder = defaultFolder ?? DefaultFolder(project);
            DataContext = this;
        }

        // ------------------------------------------------------------------
        //  Bound state
        // ------------------------------------------------------------------

        public string TargetFolder
        {
            get => _targetFolder;
            private set { if (_targetFolder != value) { _targetFolder = value; Raise(nameof(TargetFolder)); } }
        }

        public bool PerBookSubfolders
        {
            get => SubfoldersCheck.IsChecked == true;
            set
            {
                SubfoldersCheck.IsChecked = value;
                Raise(nameof(PerBookSubfolders));
                Raise(nameof(CanStart));
            }
        }

        public double Progress
        {
            get => _progress;
            private set
            {
                if (Math.Abs(_progress - value) > 0.01)
                {
                    _progress = value;
                    Raise(nameof(Progress));
                    Raise(nameof(ProgressText));
                }
            }
        }

        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (_isRunning != value)
                {
                    _isRunning = value;
                    Raise(nameof(IsRunning));
                    Raise(nameof(ProgressText));
                    Raise(nameof(CanStart));
                }
            }
        }

        public string ProgressText => IsRunning ? $"{Progress:0}%" : string.Empty;

        public int BookCount => _project.Books.Count;

        public int FileCount => _project.Books.Sum(CountFiles);

        public bool CanStart => !IsRunning && FileCount > 0;

        private static int CountFiles(Book b)
        {
            int n = 0;
            if (b.Cover != null && !b.Cover.IsEmpty) n++;
            if (b.Pages != null)
                foreach (var p in b.Pages)
                    if (p != null && !p.IsCover && !p.IsEmpty) n++;
            return n;
        }

        private static string DefaultFolder(Project project)
        {
            var name = string.IsNullOrWhiteSpace(project.OutputFolder)
                ? "PhotoBookExport"
                : Path.GetFileName(project.OutputFolder.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name)) name = "PhotoBookExport";

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), name);
        }

        // ------------------------------------------------------------------
        //  Actions
        // ------------------------------------------------------------------

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Выберите папку для экспорта",
                SelectedPath = Directory.Exists(TargetFolder)
                    ? TargetFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            TargetFolder = Path.Combine(dialog.SelectedPath, "PhotoBookExport");
        }

        private async void OnExportClick(object sender, RoutedEventArgs e)
        {
            if (IsRunning) return;

            IsRunning = true;
            Progress = 0;

            try
            {
                var options = new ExportOptions
                {
                    PerBookSubfolders = PerBookSubfolders,
                    Progress = new Progress<double>(ReportProgress)
                };

                bool ok = await _exportService.ExportProjectAsync(_project, TargetFolder, options);

                _exportSucceeded = ok;
                ExportedFolder = ok ? TargetFolder : string.Empty;

                if (!ok)
                {
                    MessageBox.Show(
                        "Не удалось скопировать файлы. Проверьте, что папка доступна для записи, и попробуйте снова.",
                        "Ошибка экспорта", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // OK closes the dialog; Cancel does not.
                DialogResult = ok;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка экспорта:\n\n{ex.Message}",
                    "Ошибка экспорта", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsRunning = false;
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            _exportSucceeded = false;
            DialogResult = false;
        }

        private void ReportProgress(double fraction)
        {
            // Progress<T> marshals to the UI thread, but guard anyway: a late report after
            // the window closed would otherwise throw.
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ReportProgress(fraction)));
                return;
            }
            Progress = fraction * 100d;
        }
    }
}
