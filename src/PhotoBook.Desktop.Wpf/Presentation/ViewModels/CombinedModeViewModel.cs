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
using PhotoBookRenamer.Application;
using PhotoBookRenamer.Infrastructure;
using PhotoBookRenamer.Presentation.Views;
using PhotoBookRenamer.Presentation.Converters;
using PhotoBookRenamer.Presentation.Dialogs;

namespace PhotoBookRenamer.Presentation.ViewModels
{
    /// <summary>State, commands, and what the shell asks of an editor.</summary>
    public partial class CombinedModeViewModel : ViewModelBase, IProjectEditor
    {
        private readonly IFileService _fileService;
        private readonly IPickFiles _files;
        private readonly IImageService _imageService;
        private readonly IThumbnailProvider _thumbnails;
        private readonly IExportService _exportService;
        private readonly ILoggingService _loggingService;
        private readonly IProjectService _projectService;
        private readonly IProjectRepository _projects;
        private readonly IProjectListService _projectListService;
        private Project? _project;
        private ProjectInfo? _currentProjectInfo;
        private string? _projectName;
        private bool _isLoading;
        private string? _errorMessage;
        private int _numberOfBooks = 1;
        private int _spreadsPerBook = 1;
        private string? _draggedFile;
        private bool _isStructureConfirmed = false;

        public CombinedModeViewModel(
            IFileService fileService,
            IPickFiles files,
            IImageService imageService,
            IThumbnailProvider thumbnails,
            IExportService exportService,
            ILoggingService loggingService,
            IProjectService projectService,
            IProjectRepository projects,
            IProjectListService projectListService)
        {
            _fileService = fileService;
            _files = files;
            _imageService = imageService;
            _thumbnails = thumbnails;
            _exportService = exportService;
            _loggingService = loggingService;
            _projectService = projectService;
            _projects = projects;
            _projectListService = projectListService;

            AvailableFiles = new ObservableCollection<string>();
            Books = new ObservableCollection<Book>();
            PhotoFiles = new ObservableCollection<PhotoFileInfo>();

            LoadFilesCommand = new AsyncRelayCommand(LoadFilesAsync);
            ClearFilesCommand = new RelayCommand(ClearFiles);
            GenerateStructureCommand = new RelayCommand(GenerateStructure);
            ConfirmStructureCommand = new RelayCommand(ConfirmStructure, () => NumberOfBooks > 0 && SpreadsPerBook > 0);
            ExportCommand = new AsyncRelayCommand(ExportAsync, () => Project?.IsValid ?? false);
            ExportWithFolderCommand = new AsyncRelayCommand(ExportWithFolderAsync, () => Project?.IsValid ?? false);
            // Enabled as soon as there is anything to save. Not tied to
            // CurrentProjectInfo: the record is created by the save itself, so tying it
            // to that left the button dead until something else happened to create it.
            SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync,
                () => Books.Count > 0 || AvailableFiles.Count > 0);
            BackCommand = new AsyncRelayCommand(BackAsync);
            DeleteFileCommand = new RelayCommand<string>(DeleteFile);
            ResetProjectCommand = new RelayCommand(ResetProject);
            DuplicateBookCommand = new RelayCommand<Book>(DuplicateBook);
            DeleteBookCommand = new RelayCommand<Book>(DeleteBook);
            DeletePageCommand = new RelayCommand<Page>(DeletePage);
            LoadPageFileCommand = new RelayCommand<Page>(LoadPageFile);
            DuplicateToAllBooksCommand = new RelayCommand<Page>(DuplicateToAllBooks);
            DeletePageFromAllBooksCommand = new RelayCommand<Page>(DeletePageFromAllBooks);
            AddBookCommand = new RelayCommand(AddBook);
            AddSpreadCommand = new RelayCommand(AddSpread);
            MovePageLeftCommand = new RelayCommand<Page>(MovePageLeft);
            MovePageRightCommand = new RelayCommand<Page>(MovePageRight);
            UndoCommand = new RelayCommand(Undo, () => false);
            RedoCommand = new RelayCommand(Redo, () => false);

            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Project))
                {
                    if (_project != null)
                    {
                        _project.PropertyChanged -= Project_PropertyChanged;
                    }

                    if (Project != null)
                    {
                        Project.PropertyChanged += Project_PropertyChanged;
                    }

                    OnPropertyChanged(nameof(CanExport));
                    UpdateExportCommands();
                }
            };

            // A growing ObservableCollection does not notify bindings, so counts and the
            // readiness label are refreshed explicitly.
            Books.CollectionChanged += (s, e) =>
            {
                // A new book has to start reporting its own "Готово N из M", and a removed
                // one has to stop. Book raises its counters for cover swaps, page
                // assignments and page add/remove, so watching the book is enough - there
                // is no need to touch every command that assigns a photo.
                if (e.NewItems != null)
                    foreach (var b in e.NewItems.OfType<Book>()) WatchBook(b);
                if (e.OldItems != null)
                    foreach (var b in e.OldItems.OfType<Book>()) UnwatchBook(b);

                UpdateRunFrameAspect();
                RefreshPhotoUsage();

                OnPropertyChanged(nameof(BooksCount));
                OnPropertyChanged(nameof(HasStructure));
                OnPropertyChanged(nameof(CurrentBookProgress));
                OnPropertyChanged(nameof(SelectedBookProgress));
                OnPropertyChanged(nameof(StructureProgress));
                OnPropertyChanged(nameof(StructureButtonText));
                OnPropertyChanged(nameof(StructureIsComplete));
                // Keep the canvas pointed at something valid after add / remove / clear.
                if (SelectedBook != null && !Books.Contains(SelectedBook))
                    SelectedBook = Books.FirstOrDefault();
                else if (SelectedBook == null)
                    SelectedBook = Books.FirstOrDefault();
                UpdateExportCommands();
            };

            AvailableFiles.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(AvailableFilesCount));
                OnPropertyChanged(nameof(HasFiles));
                // "Something to save" just became true, so the save button has to wake up.
                UpdateExportCommands();
                SyncPhotoFiles();
            };
        }

        private readonly Dictionary<Book, System.ComponentModel.PropertyChangedEventHandler> _bookWatchers = new();

        // What the shell and the panels are allowed to ask this editor to do.
        public ICommand LoadFilesCommand { get; }
        public ICommand ClearFilesCommand { get; }
        public ICommand GenerateStructureCommand { get; }
        public ICommand ConfirmStructureCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ExportWithFolderCommand { get; }
        public ICommand SaveProjectCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand DeleteFileCommand { get; }
        public ICommand ResetProjectCommand { get; }
        public ICommand DuplicateBookCommand { get; }
        public ICommand DeleteBookCommand { get; }
        public ICommand DeletePageCommand { get; }
        public ICommand LoadPageFileCommand { get; }
        public ICommand DuplicateToAllBooksCommand { get; }
        public ICommand DeletePageFromAllBooksCommand { get; }
        public ICommand AddBookCommand { get; }
        public ICommand AddSpreadCommand { get; }
        public ICommand MovePageLeftCommand { get; }
        public ICommand MovePageRightCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }

        /// <summary>
        /// Starts listening to a book's slots so the file list can tell free photos from
        /// assigned ones without every assignment command remembering to refresh it.
        /// </summary>
        private void WatchBook(Book book)
        {
            if (_bookWatchers.ContainsKey(book)) return;

            System.ComponentModel.PropertyChangedEventHandler handler = (s, e) =>
            {
                // FrameAspect is deliberately NOT in this list: it is the OUTPUT of
                // UpdateRunFrameAspect, so reacting to it calls itself forever and takes
                // the process down with a stack overflow.
                if (e.PropertyName is nameof(Book.Cover)
                                 or nameof(Book.Pages)
                                 or nameof(Book.AllSlotsPages))
                {
                    UpdateRunFrameAspect();
                    RefreshPhotoUsage();

                    // The panel's book counter is a projection over EVERY book, so it has to
                    // be republished whenever any book changes - not only when books are added
                    // or removed. It used to react to the collection alone, which is why it
                    // sat at "0 из 10" while books were quietly being finished one by one.
                    OnPropertyChanged(nameof(StructureProgress));
                    OnPropertyChanged(nameof(StructureIsComplete));
                }
            };

            _bookWatchers[book] = handler;
            book.PropertyChanged += handler;
        }

        private void UnwatchBook(Book book)
        {
            if (_bookWatchers.TryGetValue(book, out var handler))
            {
                book.PropertyChanged -= handler;
                _bookWatchers.Remove(book);
            }
        }

        private void Project_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(Project.IsValid)) return;

            // The domain raises this from whatever thread touched the book, and an automatic
            // save that resumed on the thread pool touches it there. Re-asking a command
            // from off the UI thread throws, and the save is what was unwinding.
            OnUiThread(UpdateExportCommands);
        }

        private void UpdateExportCommands()
        {
            if (ExportCommand is AsyncRelayCommand asyncCommand)
            {
                asyncCommand.NotifyCanExecuteChanged();
            }
            if (ExportWithFolderCommand is AsyncRelayCommand asyncCommand2)
            {
                asyncCommand2.NotifyCanExecuteChanged();
            }
            if (SaveProjectCommand is AsyncRelayCommand saveCommand)
            {
                saveCommand.NotifyCanExecuteChanged();
            }
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
            StopPreparation();
            _preparation = new CancellationTokenSource();
            return _preparation.Token;
        }

        internal void StopPreparation()
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
            StopPreparation();
            // An export finishes on a background thread, and the shell changes the
            // screen, so the request is marshalled to the UI thread.
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                BackToProjectListRequested?.Invoke());
        }

        AppMode IProjectEditor.Mode => AppMode.Combined;

        ICommand IProjectEditor.LoadSourceCommand => LoadFilesCommand;

        public ObservableCollection<string> AvailableFiles { get; }
        public ObservableCollection<Book> Books { get; }

        /// <summary>
        /// Counts for bindings. WPF does not re-evaluate a binding when an
        /// ObservableCollection grows, so empty states and the chip strip bind here
        /// rather than to the collections themselves.
        /// </summary>
        public int AvailableFilesCount => AvailableFiles.Count;
        public int BooksCount => Books.Count;
        public bool HasFiles => AvailableFiles.Count > 0;

        /// <summary>
        /// "Готово 2 из 4 книг" for the structure as a whole: books with every slot
        /// filled. It sits under the project name where the unique mode puts "1 обложка
        /// • 4 разворота", so both screens answer "how far along am I" the same way.
        /// </summary>
        public string StructureProgress => Books.Count == 0
            ? "Готово 0 книг"
            : $"Готово {Books.Count(b => b.IsFilled)} из {Books.Count} книг";

        /// <summary>
        /// The one structure button. Same control either way, only the wording changes -
        /// the panel must not reshape itself when the first book appears.
        /// </summary>
        public string StructureButtonText => Books.Count == 0 ? "Добавить структуру" : "Обновить структуру";

        /// <summary>
        /// Every book of the run has every slot filled. Drives the readiness badge colour:
        /// grey while there is work left, green when the run is done.
        /// </summary>
        public bool StructureIsComplete => Books.Count > 0 && Books.All(b => b.IsFilled);

        // ------------------------------------------------------------------
        //  The file list, as the narrow left column shows it
        // ------------------------------------------------------------------

        /// <summary>
        /// The loaded photos with a name, a pixel size and a free/assigned/shared state.
        ///
        /// Derived from <see cref="AvailableFiles"/> and the books rather than stored: the
        /// project file keeps its plain path list, so old projects open untouched and a
        /// photo that turns out to be shared needs no flag on disk.
        /// </summary>
        public ObservableCollection<PhotoFileInfo> PhotoFiles { get; }

        private string _photoSearch = string.Empty;

        /// <summary>Filters the list by file name; empty shows everything.</summary>
        public string PhotoSearch
        {
            get => _photoSearch;
            set
            {
                if (SetProperty(ref _photoSearch, value ?? string.Empty))
                    OnPropertyChanged(nameof(FilteredPhotoFiles));
            }
        }

        public IEnumerable<PhotoFileInfo> FilteredPhotoFiles
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_photoSearch)) return PhotoFiles;
                return PhotoFiles.Where(f =>
                    f.Name.IndexOf(_photoSearch, StringComparison.CurrentCultureIgnoreCase) >= 0);
            }
        }
    }
}
