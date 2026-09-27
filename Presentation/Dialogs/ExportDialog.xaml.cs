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

        /// <summary>
        /// The per-book-subfolders choice, held here and NOT read back off the control.
        ///
        /// It used to be `get => SubfoldersCheck.IsChecked == true` with a setter that wrote
        /// `SubfoldersCheck.IsChecked = value` - a two-way binding whose source wrote the
        /// target back, which is a binding loop. Ticking the box threw, which is what the
        /// owner hit: the option is useless if clicking it crashes the dialog.
        /// </summary>
        private bool _perBookSubfolders;

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

            if (_project.Mode == AppMode.Combined)
            {
                // Per-book subfolders would put a shared 000-FF file in one book's folder and
                // break the run-wide default, so the option is not offered in this mode.
                SubfoldersPanel.Visibility = Visibility.Collapsed;
                var plan = CombinedExportPlanner.Build(_project, _exportService.GenerateFileName);
                PlanSummary.Text = BuildPlanSummary(plan);
                PlanSummary.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Says what the run will produce, before anything is copied: how many files, how
        /// many of them shared, and whether the size of the run reaches the site.
        /// </summary>
        private static string BuildPlanSummary(ExportPlan plan)
        {
            if (plan.Entries.Count == 0)
                return "Файлы для экспорта не найдены.";

            var text =
                $"Книг: {plan.BookCount}. Файлов: {plan.Entries.Count} — общих {plan.SharedCount}, по книгам {plan.PerBookCount}.";

            if (plan.Entries.Any(e => e.CarriesBookCount))
                text += " Количество книг передаётся отдельным файлом с номером последней книги.";

            if (plan.Warning != null)
                text += " " + plan.Warning;

            return text;
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
            get => _perBookSubfolders;
            set
            {
                if (_perBookSubfolders == value) return;
                _perBookSubfolders = value;
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

            // The base name is only a starting point: the real target is always the first
            // free PhotoBookExportN under it, so no two exports can land in one folder.
            return UniqueExportFolder(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), name);
        }

        /// <summary>
        /// The first folder named <c>base</c>, <c>base1</c>, <c>base2</c>, ... under
        /// <paramref name="parent"/> that does not exist yet.
        ///
        /// The owner's rule: every export gets its own folder, so exporting the same project
        /// five times leaves five folders and two runs can never be mixed up. The plain name
        /// is used while it is free and the index is added from 1, so the first export on a
        /// clean desktop is <c>PhotoBookExport</c> and not <c>PhotoBookExport0</c>.
        ///
        /// Shared by the default and by "Обзор": choosing a parent by hand must not be a way
        /// to get the old overwrite-everything behaviour back.
        /// </summary>
        internal static string UniqueExportFolder(string parent, string @base = "PhotoBookExport")
        {
            var root = Path.Combine(parent, @base);
            if (!Directory.Exists(root) && !File.Exists(root)) return root;

            for (int i = 1; i < 10_000; i++)
            {
                var candidate = Path.Combine(parent, $"{@base}{i}");
                if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
            }

            // Ten thousand exports into one parent is not a case worth a different failure
            // mode; fall back to a timestamp rather than overwriting somebody's folder.
            return Path.Combine(parent, $"{@base}-{DateTime.Now:yyyyMMdd-HHmmss}");
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

            // Still a fresh folder inside the chosen parent - see UniqueExportFolder.
            TargetFolder = UniqueExportFolder(dialog.SelectedPath);
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
