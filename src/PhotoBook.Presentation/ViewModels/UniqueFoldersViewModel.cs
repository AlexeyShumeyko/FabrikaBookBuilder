using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoBook.Core;
using PhotoBook.Application;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>State, commands, and what the shell asks of an editor.</summary>
    public partial class UniqueFoldersViewModel : ViewModelBase, IProjectEditor
    {
        private readonly IFileService _fileService;
        private readonly IPickFiles _files;
        private readonly IImageService _imageService;
        private readonly IThumbnailProvider _thumbnails;
        private readonly IDecodedImageCache _images;
        private readonly IProjectDialogs _dialogs;
        private readonly IEditorDialogs _editorDialogs;
        private readonly IProjectBuilder _projectBuilder;
        private readonly IEditHistory _editHistory;
        private readonly IProjectRepository _projects;
        private readonly IExportService _exportService;
        private readonly ILoggingService _loggingService;
        private readonly IProjectListService _projectListService;
        private Project? _project;
        private ProjectInfo? _currentProjectInfo;
        private bool _isLoading;
        private string? _errorMessage;
        private Book? _selectedBook;

        /// <summary>
        /// Count for bindings. See the CollectionChanged subscription in the constructor:
        /// binding straight to <see cref="Books"/> would not update when books are added.
        /// </summary>
        public int BooksCount => Books.Count;

        public bool CanExport => Project?.IsValid ?? false;

        /// <summary>
        /// Counts for the project panel: "2 обложек · 6 разворотов · 2 папок".
        ///
        /// All three are always shown, zeros included - a project the user has just
        /// started should read "0 обложек · 0 разворотов · 0 папок" rather than switch to
        /// a different sentence, so the numbers stay put and only their values move.
        /// One folder becomes one book, so the folder count is the book count.
        /// </summary>
        public string BooksSummary
        {
            get
            {
                int covers = 0, spreads = 0;
                foreach (var b in Books)
                {
                    if (b.Cover != null && !b.Cover.IsEmpty) covers++;
                    if (b.Pages != null)
                        foreach (var p in b.Pages)
                            if (p != null && !p.IsCover && !p.IsEmpty) spreads++;
                }

                return $"{covers} {Plural(covers, "обложка", "обложки", "обложек")} · " +
                       $"{spreads} {Plural(spreads, "разворот", "разворота", "разворотов")} · " +
                       $"{Books.Count} {Plural(Books.Count, "папка", "папки", "папок")}";
            }
        }

        private static string Plural(int n, string one, string few, string many)
        {
            int mod10 = n % 10;
            int mod100 = n % 100;
            if (mod10 == 1 && mod100 != 11) return one;
            if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return few;
            return many;
        }

        public ProjectInfo? CurrentProjectInfo
        {
            get => _currentProjectInfo;
            private set
            {
                if (SetProperty(ref _currentProjectInfo, value))
                {
                    if (SaveProjectCommand is AsyncRelayCommand saveCmd)
                    {
                        saveCmd.NotifyCanExecuteChanged();
                    }
                }
            }
        }

        public UniqueFoldersViewModel(
            IFileService fileService,
            IPickFiles files,
            IImageService imageService,
            IThumbnailProvider thumbnails,
            IProjectBuilder projectBuilder,
        IEditHistory editHistory,
            IProjectRepository projects,
            IExportService exportService,
            ILoggingService loggingService,
            IProjectListService projectListService,
            IDecodedImageCache images,
            IUiThread ui,
            IProjectDialogs dialogs,
            IEditorDialogs editorDialogs)
        {
            _fileService = fileService;
            _files = files;
            _imageService = imageService;
            _thumbnails = thumbnails;
            _images = images;
            Ui = ui;
            _dialogs = dialogs;
            _editorDialogs = editorDialogs;
            _projectBuilder = projectBuilder;
            _editHistory = editHistory;
            _projects = projects;
            _exportService = exportService;
            _loggingService = loggingService;
            _projectListService = projectListService;

            Books = new ObservableCollection<Book>();
            LoadFoldersCommand = new AsyncRelayCommand(LoadFoldersAsync);
            SelectCoverCommand = new RelayCommand<Book>(SelectCoverAsync);
            ExportCommand = new AsyncRelayCommand(ExportAsync, () => Project?.IsValid ?? false);
            ExportWithFolderCommand = new AsyncRelayCommand(ExportWithFolderAsync, () => Project?.IsValid ?? false);
            BackCommand = new AsyncRelayCommand(async () =>
            {
                if (CurrentProjectInfo != null)
                {
                    var wasSaved = !string.IsNullOrEmpty(CurrentProjectInfo.FilePath) &&
                                   System.IO.File.Exists(CurrentProjectInfo.FilePath);

                    // Nothing was saved and there is nothing in it, so it goes: an empty
                    // project in the list is a card that opens onto nothing.
                    if (!wasSaved && (Project == null || Project.Books == null || Project.Books.Count == 0))
                    {
                        await _projectListService.DeleteProjectAsync(CurrentProjectInfo);
                    }
                }

                RequestBackToProjectList();
            });
            SaveProjectCommand = new AsyncRelayCommand(SaveAndCloseAsync);
            DeleteBookCommand = new RelayCommand<Book>(DeleteBook);
            ResetProjectCommand = new RelayCommand(ResetProject);
            ResetBookOrderCommand = new RelayCommand<Book>(ResetBookOrder);
            UndoCommand = new RelayCommand(Undo, () => _editHistory.CanUndo);
            RedoCommand = new RelayCommand(Redo, () => _editHistory.CanRedo);
            MovePageUpCommand = new RelayCommand<Page>(MovePageUp);
            MovePageDownCommand = new RelayCommand<Page>(MovePageDown);
            AssignPageNumberCommand = new RelayCommand<Page>(AssignPageNumber);
            AssignCoverCommand = new RelayCommand<Page>(AssignCover);

            // WPF only re-evaluates a binding when the SOURCE raises PropertyChanged.
            // An ObservableCollection growing is not enough, so counts are surfaced
            // explicitly for the empty state and the toolbar summary.
            Books.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(BooksCount));
                OnPropertyChanged(nameof(BooksSummary));
            };

            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Project))
                {
                    // SetProject is async and may be handed a project from a continuation, so
                    // this can arrive off the UI thread - where a command cannot be re-asked.
                    RunOnUi(() =>
                    {
                        OnPropertyChanged(nameof(CanExport));
                        OnPropertyChanged(nameof(BooksSummary));
                        if (ExportCommand is AsyncRelayCommand exportCmd)
                            exportCmd.NotifyCanExecuteChanged();
                        if (ExportWithFolderCommand is AsyncRelayCommand exportFolderCmd)
                            exportFolderCmd.NotifyCanExecuteChanged();
                    });
                }
                if (e.PropertyName == nameof(CurrentProjectInfo))
                {
                    RunOnUi(() =>
                    {
                        if (SaveProjectCommand is AsyncRelayCommand saveCmd)
                            saveCmd.NotifyCanExecuteChanged();
                    });
                }
            };
        }

        /// <summary>
        /// Asks the shell to go back to the project list. Raised rather than routed
        /// through the shell object: this editor is created before the shell exists, so
        /// a direct reference would be circular, and a lookup at the moment of the click
        /// would be a failure that shows up as a button that does nothing.
        /// </summary>
        public event Action? BackToProjectListRequested;


        /// <summary>
        /// Prepares what the next screen will need: reduced copies and decoded bitmaps.
        /// Measured on twenty books it is the longest thing the program does, and nobody
        /// is waiting for it - the window is already usable while it runs. So it is
        /// cancelled the moment it stops being wanted: a new preparation supersedes it,
        /// and leaving the project or resetting it cancels it outright.
        /// </summary>
        private CancellationTokenSource? _preparation;

        private CancellationToken BeginPreparation()
        {
            StopPreparing();
            _preparation = new CancellationTokenSource();
            return _preparation.Token;
        }

        public void StopPreparing()
        {
            var previous = _preparation;
            _preparation = null;
            if (previous == null) return;
            try
            {
                previous.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already gone: nothing was running.
            }
            finally
            {
                previous.Dispose();
            }
        }
        private void RequestBackToProjectList()
        {
            StopPreparing();
            // An export finishes on a background thread, and the shell changes the
            // screen, so the request is marshalled to the UI thread.
            Ui.Invoke(() =>
                BackToProjectListRequested?.Invoke());
        }

        AppMode IProjectEditor.Mode => AppMode.UniqueFolders;

        ICommand IProjectEditor.LoadSourceCommand => LoadFoldersCommand;

        private string? _projectName;

        public string? ProjectName
        {
            get => _projectName;
            set
            {
                if (SetProperty(ref _projectName, value))
                {
                    // FallbackValue in XAML only fires when a path fails to resolve, not
                    // when the resolved value is null, so the placeholder lives here.
                    OnPropertyChanged(nameof(DisplayProjectName));

                    if (CurrentProjectInfo != null)
                    {
                        CurrentProjectInfo.Name = value ?? string.Empty;
                    }
                }
            }
        }

        public string DisplayProjectName =>
            string.IsNullOrWhiteSpace(ProjectName) ? "Новый проект" : ProjectName!;

        /// <inheritdoc />
        public ProjectInfo? OpenProject => CurrentProjectInfo;

        // What the shell and the panels are allowed to ask this editor to do.
        public ICommand LoadFoldersCommand { get; }
        public ICommand SelectCoverCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ExportWithFolderCommand { get; }
        public ICommand BackCommand { get; private set; }
        public ICommand SaveProjectCommand { get; }
        public ICommand DeleteBookCommand { get; }
        public ICommand ResetProjectCommand { get; }

        /// <summary>Re-sorts one book's spreads back to alphabetical by file name.</summary>
        public ICommand ResetBookOrderCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }
        public ICommand MovePageUpCommand { get; }
        public ICommand MovePageDownCommand { get; }
        public ICommand AssignPageNumberCommand { get; }

        /// <summary>Promotes a spread to be the book's cover, keeping the file it points at.</summary>
        public ICommand AssignCoverCommand { get; }
    }
}
