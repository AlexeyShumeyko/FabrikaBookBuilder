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
using Microsoft.Extensions.DependencyInjection;
using PhotoBookRenamer.Domain;
using PhotoBookRenamer.Application;
using PhotoBookRenamer.Infrastructure;
using PhotoBookRenamer.Presentation.Views;
using PhotoBookRenamer.Presentation.Converters;using PhotoBookRenamer.Presentation.Dialogs;

namespace PhotoBookRenamer.Presentation.ViewModels
{
    public class CombinedModeViewModel : ViewModelBase
    {
        private readonly IFileService _fileService;
        private readonly IImageService _imageService;
        private readonly IExportService _exportService;
        private readonly ILoggingService _loggingService;
        private readonly IProjectService _projectService;
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
            IImageService imageService,
            IExportService exportService,
            ILoggingService loggingService,
            IProjectService projectService,
            IProjectListService projectListService)
        {
            _fileService = fileService;
            _imageService = imageService;
            _exportService = exportService;
            _loggingService = loggingService;
            _projectService = projectService;
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
            OpenHelpCommand = new RelayCommand(OpenHelp);
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

        /// <summary>
        /// Starts listening to a book's slots so the file list can tell free photos from
        /// assigned ones without every assignment command remembering to refresh it.
        /// </summary>
        private void WatchBook(Book book)
        {
            if (_bookWatchers.ContainsKey(book)) return;

            System.ComponentModel.PropertyChangedEventHandler handler = (s, e) =>
            {
                if (e.PropertyName is nameof(Book.Cover)
                                 or nameof(Book.Pages)
                                 or nameof(Book.AllSlotsPages))
                {
                    UpdateRunFrameAspect();
                    RefreshPhotoUsage();
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
            if (e.PropertyName == nameof(Project.IsValid))
            {
                UpdateExportCommands();
            }
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

        private AppMode CurrentMode
        {
            set
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    // MainViewModel is a singleton and is already the MainWindow's
                    // DataContext, so flipping the mode is enough. Re-assigning
                    // DataContext here used to rebuild the persistent header.
                    var serviceProvider = ((App)System.Windows.Application.Current).GetServiceProvider();
                    var mainVm = serviceProvider?.GetRequiredService<MainViewModel>();
                    if (mainVm != null)
                    {
                        mainVm.CurrentMode = value;
                    }
                });
            }
        }

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

        /// <summary>
        /// Mirrors AvailableFiles into <see cref="PhotoFiles"/>. Entries that survive keep
        /// the size already read for them, so re-syncing after a drop does not re-measure
        /// hundreds of files; only the new ones are measured, off the UI thread.
        /// </summary>
        private void SyncPhotoFiles()
        {
            var wanted = AvailableFiles
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var present = new HashSet<string>(wanted, StringComparer.OrdinalIgnoreCase);
            for (int i = PhotoFiles.Count - 1; i >= 0; i--)
            {
                if (!present.Contains(PhotoFiles[i].Path))
                    PhotoFiles.RemoveAt(i);
            }

            var toMeasure = new List<PhotoFileInfo>();
            foreach (var path in wanted)
            {
                bool exists = PhotoFiles.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
                if (exists) continue;

                var info = new PhotoFileInfo(path);
                PhotoFiles.Add(info);
                toMeasure.Add(info);
            }

            OnPropertyChanged(nameof(FilteredPhotoFiles));
            RefreshPhotoUsage();

            if (toMeasure.Count > 0)
                _ = MeasurePhotoFilesAsync(toMeasure);
        }

        /// <summary>Reads each new file's pixel size for the caption in the list.</summary>
        private async Task MeasurePhotoFilesAsync(List<PhotoFileInfo> files)
        {
            foreach (var file in files)
            {
                try
                {
                    var (width, height) = await _imageService.GetImageDimensionsAsync(file.Path);
                    file.SetDimensions(width, height);
                }
                catch
                {
                    // A file that cannot be measured keeps the "…" placeholder; the photo
                    // itself still loads in a slot, and the page's own size decides the
                    // frame, so this caption is not worth failing over.
                }
            }
        }

        // ------------------------------------------------------------------
        //  One frame shape for the whole run
        // ------------------------------------------------------------------

        /// <summary>
        /// Decides the frame shape for every book of the run: the MEDIAN of every spread
        /// in the run, the cover never counted.
        ///
        /// Both of the owner's requirements fall out of a median rather than a latch.
        /// A run is aligned by its spreads, so a 1.9 cover next to square spreads must not
        /// drag the frames - and it does not, because it is not in the set. And the median
        /// of a run of equally shaped spreads IS that shape, so loading photo after photo
        /// stops moving the cards after the first one - which a frozen value also did, but
        /// a frozen value could not recover: delete the books, build the run again, and
        /// the frames stayed in the old format while the new photos were a different one.
        ///
        /// Recomputed on every change, so a wrong photo loaded by mistake is outnumbered
        /// by the right ones and the frames follow the content again.
        /// </summary>
        private void UpdateRunFrameAspect()
        {
            var ratios = new List<double>();

            foreach (var book in Books)
            {
                foreach (var page in book.Pages)
                {
                    if (page == null || page.IsCover || page.IsEmpty || !page.HasDimensions) continue;
                    ratios.Add(page.AspectRatio);
                }
            }

            // Nothing measured yet: a cover may still say something useful, otherwise the
            // default 16:10. Once a spread arrives the cover stops counting.
            if (ratios.Count == 0)
            {
                foreach (var book in Books)
                {
                    if (book.Cover != null && !book.Cover.IsEmpty && book.Cover.HasDimensions)
                    {
                        ratios.Add(book.Cover.AspectRatio);
                        break;
                    }
                }
            }

            StampRunAspect(ratios.Count > 0 ? MedianOf(ratios) : 1.6);
        }

        private static double MedianOf(List<double> values)
        {
            values.Sort();
            int mid = values.Count / 2;
            return values.Count % 2 == 1
                ? values[mid]
                : (values[mid - 1] + values[mid]) / 2d;
        }

        private void StampRunAspect(double aspect)
        {
            foreach (var book in Books)
                book.ApplyFrameAspect(aspect);
        }

        /// <summary>
        /// Nothing to forget: the run's shape is derived from the photos that are actually
        /// in the books right now, so an emptied project falls back to the default on its
        /// own. The latch that used to sit here is what left a rebuilt run in the old
        /// format.
        /// </summary>
        /// <summary>
        /// Counts how many slots hold each photo and pushes the counts into the list.
        /// "Общий" is therefore a fact about the run, not a flag anyone has to maintain:
        /// applying a photo to the same position of every book is all it takes.
        ///
        /// Two counts, because the file list and the slot mean different things by shared:
        /// the list counts SLOTS (a photo used twice anywhere is not free any more), while
        /// a slot is "shared" only when another BOOK holds the same photo. The same file
        /// twice inside one book is a duplicate, not a run-wide spread.
        /// </summary>
        private void RefreshPhotoUsage()
        {
            var slotCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var bookCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var book in Books)
            {
                // Counted per book, so one photo used in three books adds 3 to both
                // dictionaries no matter how many slots of that book hold it.
                var inThisBook = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (book.Cover != null && !book.Cover.IsEmpty && !string.IsNullOrEmpty(book.Cover.SourcePath))
                    inThisBook.Add(book.Cover.SourcePath);

                foreach (var page in book.Pages)
                {
                    if (page == null || page.IsEmpty || string.IsNullOrEmpty(page.SourcePath)) continue;
                    inThisBook.Add(page.SourcePath);
                    slotCounts.TryGetValue(page.SourcePath, out int n);
                    slotCounts[page.SourcePath] = n + 1;
                }

                if (book.Cover != null && !book.Cover.IsEmpty && !string.IsNullOrEmpty(book.Cover.SourcePath))
                {
                    slotCounts.TryGetValue(book.Cover.SourcePath, out int c);
                    slotCounts[book.Cover.SourcePath] = c + 1;
                }

                foreach (var path in inThisBook)
                {
                    bookCounts.TryGetValue(path, out int n);
                    bookCounts[path] = n + 1;
                }
            }

            foreach (var file in PhotoFiles)
                file.SetUsage(slotCounts.TryGetValue(file.Path, out int n) ? n : 0);

            foreach (var book in Books)
            {
                if (book.Cover != null)
                    book.Cover.IsShared = IsShared(book.Cover);

                foreach (var page in book.Pages)
                {
                    if (page != null) page.IsShared = IsShared(page);
                }
            }

            bool IsShared(Domain.Page page) =>
                !string.IsNullOrEmpty(page.SourcePath) &&
                bookCounts.TryGetValue(page.SourcePath, out int books) && books >= 2;
        }

        public bool CanExport => Project?.IsValid ?? false;

        /// <summary>
        /// The book shown in the assembly canvas. The reference shows one book at a time
        /// with a chip strip to switch, instead of every book stacked vertically.
        /// Set by the ListBox's SelectedItem binding.
        /// </summary>
        public Book? SelectedBook
        {
            get => _selectedBook;
            set
            {
                if (SetProperty(ref _selectedBook, value))
                {
                    OnPropertyChanged(nameof(HasStructure));
                    OnPropertyChanged(nameof(SelectedBookProgress));
                }
            }
        }

        private Book? _selectedBook;

        public bool HasStructure => Books.Count > 0;

        public string SelectedBookProgress =>
            SelectedBook != null
                ? $"Готово {FilledSlots(SelectedBook)} из {TotalSlots(SelectedBook)}"
                : "Книг пока нет";

        /// <summary>"Готово 2 из 4" style readiness label for the first book.</summary>
        public string CurrentBookProgress =>
            Books.Count > 0 ? $"Готово {FilledSlots(Books[0])} из {TotalSlots(Books[0])}" : "Книг пока нет";

        private static int FilledSlots(Book b)
        {
            int n = 0;
            if (b.Cover != null && !b.Cover.IsEmpty) n++;
            foreach (var p in b.Pages)
                if (p != null && !p.IsCover && !p.IsEmpty) n++;
            return n;
        }

        private static int TotalSlots(Book b)
        {
            int n = b.Cover != null ? 1 : 0;
            foreach (var p in b.Pages)
                if (p != null && !p.IsCover) n++;
            return n;
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
                }
            }
        }

        public string DisplayProjectName =>
            string.IsNullOrWhiteSpace(ProjectName) ? "Новый проект" : ProjectName!;

        public async System.Threading.Tasks.Task SaveProjectNameOnlyAsync()
        {
            if (CurrentProjectInfo == null || string.IsNullOrEmpty(ProjectName))
            {
                return;
            }

            try
            {
                CurrentProjectInfo.Name = ProjectName;
                CurrentProjectInfo.LastModified = DateTime.Now;
                await _projectListService.SaveProjectInfoAsync(CurrentProjectInfo);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Ошибка сохранения названия проекта", ex);
            }
        }

        public bool IsStructureConfirmed
        {
            get => _isStructureConfirmed;
            set => SetProperty(ref _isStructureConfirmed, value);
        }

        private System.Threading.Timer? _generateTimer;

        public int NumberOfBooks
        {
            get => _numberOfBooks;
            set
            {
                if (value < 1) value = 1;
                if (value > 99) value = 99;
                
                if (SetProperty(ref _numberOfBooks, value))
                {
                    IsStructureConfirmed = false;
                    if (ConfirmStructureCommand is RelayCommand confirmCmd)
                    {
                        confirmCmd.NotifyCanExecuteChanged();
                    }
                }
            }
        }

        public int SpreadsPerBook
        {
            get => _spreadsPerBook;
            set
            {
                if (value < 1) value = 1;
                if (value > 99) value = 99;
                
                if (SetProperty(ref _spreadsPerBook, value))
                {
                    if (IsStructureConfirmed && Books.Count > 0)
                    {
                        SynchronizeSpreadsInAllBooks();
                    }
                    else
                    {
                        IsStructureConfirmed = false;
                        if (ConfirmStructureCommand is RelayCommand confirmCmd)
                        {
                            confirmCmd.NotifyCanExecuteChanged();
                        }
                    }
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public Project? Project
        {
            get => _project;
            set => SetProperty(ref _project, value);
        }

        public string? DraggedFile
        {
            get => _draggedFile;
            set => SetProperty(ref _draggedFile, value);
        }

        /// <summary>
        /// Raised after a whole-project export has finished and the success dialog was
        /// dismissed. MainViewModel ends the project session on it and returns to the
        /// project list.
        /// </summary>
        public event EventHandler? ProjectExported;

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
        public ICommand OpenHelpCommand { get; }
        public ICommand AddSpreadCommand { get; }
        public ICommand MovePageLeftCommand { get; }
        public ICommand MovePageRightCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }

        private async Task LoadFilesAsync()
        {
            ErrorMessage = null;

            try
            {
                // IsLoading is raised AFTER the picker, not around it. The picker is its own
                // progress indicator, and the veil used to cover the whole window while it
                // was open - which is where the owner saw a grey background appear.
                var files = await _fileService.SelectFilesAsync();

                if (files == null || files.Length == 0)
                    return;

                IsLoading = true;
                try
                {
                    // Первое действие с данными создаёт запись проекта — до этого момента
                    // вкладка могла быть открыта без какого-либо проекта вообще.
                    await EnsureProjectInfoAsync();

                    foreach (var file in files)
                    {
                        if (_fileService.IsJpegFile(file) && !AvailableFiles.Contains(file))
                        {
                            AvailableFiles.Add(file);
                        }
                    }

                    _ = Task.Run(async () => await _imageService.LoadThumbnailsAsync(AvailableFiles));
                }
                finally
                {
                    IsLoading = false;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка: {ex.Message}";
                _loggingService.LogError("Ошибка загрузки файлов", ex);
            }
        }

        private void ClearFiles()
        {
            AvailableFiles.Clear();
        }

        private void GenerateStructure()
        {
            if (!IsStructureConfirmed)
            {
                return;
            }
            
            if (Books.Count == 0)
            {
                for (int i = 0; i < NumberOfBooks; i++)
                {
                    var book = new Book
                    {
                        BookIndex = i + 1,
                        Name = $"Книга {i + 1}",
                        Cover = new Page { IsCover = true, Index = 0 }
                    };

                    for (int j = 0; j < SpreadsPerBook; j++)
                    {
                        book.Pages.Add(new Page { IsCover = false, Index = j + 1, DisplayIndex = j + 1 });
                    }

                    book.UpdatePageSlots();
                    Books.Add(book);
                }
                
                OnPropertyChanged(nameof(Books));
            }
            else
            {
                var currentCount = Books.Count;

                // Growing the run is silent, shrinking it is not. Checked before anything
                // is touched, so cancelling leaves the structure exactly as it was.
                if (!ConfirmStructureChange(NumberOfBooks, SpreadsPerBook))
                    return;

                if (NumberOfBooks > currentCount)
                {
                    for (int i = currentCount; i < NumberOfBooks; i++)
                    {
                        var book = new Book
                        {
                            BookIndex = i + 1,
                            Name = $"Книга {i + 1}",
                            Cover = new Page { IsCover = true, Index = 0 }
                        };

                        // Straight from the requested count, not from the first book: every
                        // book of a run has the same number of spreads, and that is the
                        // whole point of the structure.
                        for (int j = 0; j < SpreadsPerBook; j++)
                        {
                            book.Pages.Add(new Page { IsCover = false, Index = j + 1, DisplayIndex = j + 1 });
                        }

                        book.UpdatePageSlots();
                        Books.Add(book);
                    }
                }
                else if (NumberOfBooks < currentCount)
                {
                    // From the end: the last books go, so the numbering of the ones that
                    // stay does not shift under the photographer.
                    while (Books.Count > NumberOfBooks)
                    {
                        Books.RemoveAt(Books.Count - 1);
                    }
                }
                
                SynchronizeSpreadsInAllBooks();
                
                for (int i = 0; i < Books.Count; i++)
                {
                    Books[i].BookIndex = i + 1;
                    Books[i].Name = $"Книга {i + 1}";
                }

                OnPropertyChanged(nameof(StructureProgress));
                OnPropertyChanged(nameof(StructureIsComplete));
            }

            if (Project == null)
            {
                Project = new Project
                {
                    Mode = AppMode.Combined
                };
            }
            
            Project.Books.Clear();
            foreach (var book in Books)
            {
                Project.Books.Add(book);
            }
            
            UpdateExportCommands();
        }
        
        /// <summary>
        /// Asks before the structure gets SMALLER; growing it needs no question.
        ///
        /// The message says how many photos are about to disappear, because the numbers in
        /// the two fields look harmless until the last spread of a book turns out to be
        /// filled. Removal always happens from the end: the last books, the last spreads.
        /// </summary>
        private bool ConfirmStructureChange(int targetBooks, int targetSpreads)
        {
            int booksLost = Books.Count - targetBooks;
            int spreadsLost = Books.Count == 0
                ? 0
                : Math.Max(0, Books.Max(b => b.Pages.Count(p => !p.IsCover)) - targetSpreads);

            if (booksLost <= 0 && spreadsLost <= 0) return true;

            int photosLost = 0;
            for (int i = 0; i < Books.Count; i++)
            {
                var book = Books[i];

                if (i >= targetBooks)
                {
                    // This whole book goes away, with everything already put in it.
                    photosLost += book.FilledSlotCount;
                    continue;
                }

                foreach (var page in book.Pages.Where(p => !p.IsCover && p.Index > targetSpreads))
                {
                    if (!page.IsEmpty) photosLost++;
                }
            }

            var parts = new List<string>();
            if (booksLost > 0)
                parts.Add($"книг: {booksLost}");

            if (spreadsLost > 0)
            {
                parts.Add($"разворотов в книге: {spreadsLost}");
                // Say the position that disappears, since that is what the user counts in.
                var firstLost = targetSpreads + 1;
                parts.Add($"последние развороты начиная с {firstLost}");
            }

            string photoLine = photosLost > 0
                ? $"\n\nВ них уже стоят фото — они будут удалены: {photosLost}."
                : "\n\nПустых слотов в удаляемом нет.";

            string message =
                $"Уменьшить структуру до {targetBooks} книг по {targetSpreads} разворотов?" +
                $"\n\nБудет удалено с конца — {string.Join("; ", parts)}.{photoLine}" +
                "\n\nОтменить изменение?";

            return System.Windows.MessageBox.Show(
                       message,
                       "Подтверждение изменения структуры",
                       System.Windows.MessageBoxButton.YesNo,
                       System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;
        }

        private void SynchronizeSpreadsInAllBooks()
        {
            if (Books.Count == 0) return;
            
            
            foreach (var book in Books)
            {
                var currentSpreads = book.Pages.Count(p => !p.IsCover);
                
                if (currentSpreads < SpreadsPerBook)
                {
                    for (int i = currentSpreads; i < SpreadsPerBook; i++)
                    {
                        var newIndex = i + 1;
                        book.Pages.Add(new Page { IsCover = false, Index = newIndex, DisplayIndex = newIndex });
                    }
                }
                else if (currentSpreads > SpreadsPerBook)
                {
                    var pagesToRemove = book.Pages.Where(p => !p.IsCover && p.Index > SpreadsPerBook).ToList();
                    foreach (var page in pagesToRemove)
                    {
                        if (string.IsNullOrEmpty(page.SourcePath))
                        {
                            book.Pages.Remove(page);
                        }
                    }
                    
                    while (book.Pages.Count(p => !p.IsCover) > SpreadsPerBook)
                    {
                        var lastPage = book.Pages.Where(p => !p.IsCover).OrderByDescending(p => p.Index).FirstOrDefault();
                        if (lastPage != null)
                        {
                            book.Pages.Remove(lastPage);
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                
                var spreads = book.Pages.Where(p => !p.IsCover).OrderBy(p => p.Index).ToList();
                for (int i = 0; i < spreads.Count; i++)
                {
                    spreads[i].Index = i + 1;
                    spreads[i].DisplayIndex = i + 1;
                }
                
                book.UpdatePageSlots();
            }
            
        }

        private void ConfirmStructure()
        {
            IsStructureConfirmed = true;
            GenerateStructure();
            
            if (ConfirmStructureCommand is RelayCommand confirmCmd)
            {
                confirmCmd.NotifyCanExecuteChanged();
            }
        }

        /// <summary>
        /// Copies a measured size from one slot to another showing the same file. Cheaper
        /// and safer than re-reading the file, and it is how a run of shared spreads gets
        /// the right frame in every book at once.
        /// </summary>
        private static void CopyDimensions(Domain.Page from, Domain.Page to)
        {
            if (!from.HasDimensions || to.HasDimensions) return;
            if (!string.Equals(from.SourcePath, to.SourcePath, StringComparison.OrdinalIgnoreCase)) return;

            to.ImageWidth = from.ImageWidth;
            to.ImageHeight = from.ImageHeight;
        }

        /// <summary>
        /// Fills a slot's pixel size once a photo lands in it, then re-shapes the book.
        ///
        /// The frame ratio is the median of a book's own photo sizes, so a photo dropped
        /// into a slot without its size left the whole book on the default 16:10 - that is
        /// the "frames did not adapt" symptom. Fire-and-forget, exactly like the thumbnail
        /// load beside it: the photo appears at once and the frame follows a moment later.
        /// </summary>
        private async Task FillSlotDimensionsAsync(Domain.Page? page)
        {
            if (page == null || page.HasDimensions || string.IsNullOrEmpty(page.SourcePath)) return;

            try
            {
                var (width, height) = await _imageService.GetImageDimensionsAsync(page.SourcePath);
                if (width <= 0 || height <= 0) return;

                page.ImageWidth = width;
                page.ImageHeight = height;

                var book = Books.FirstOrDefault(b => b.Cover == page || b.Pages.Contains(page));
                book?.UpdatePageSlots();

                OnPropertyChanged(nameof(Books));
            }
            catch (Exception ex)
            {
                // The photo itself still shows; only the frame stays on the default ratio.
                _loggingService.LogError("Не удалось прочитать размер фото для слота", ex);
            }
        }

        public void DropFileOnSlot(Domain.Page page, string filePath, DropAction action, List<Book>? selectedBooks = null)
        {
            if (page == null || string.IsNullOrEmpty(filePath))
            {
                return;
            }

            if (action == DropAction.ThisBookOnly)
            {
                var book = Books.FirstOrDefault(b => b.Cover == page || b.Pages.Contains(page));
                var targetBook = Books.FirstOrDefault(b => b.Cover == page || b.Pages.Contains(page));
                
                if (!string.IsNullOrEmpty(page.SourcePath) && page.SourcePath != filePath)
                {
                    Presentation.Converters.PageSourceConverter.ClearCacheForFile(page.SourcePath);
                }
                
                Presentation.Converters.PageSourceConverter.ClearCacheForFile(filePath);
                page.SourcePath = filePath;
                
                if (targetBook != null)
                {
                    targetBook.UpdatePageSlots();
                }
                
                OnPropertyChanged(nameof(Books));
                LoadThumbnailForPage(page);
                _ = FillSlotDimensionsAsync(page);
                UpdateExportCommands();
            }
            else if (action == DropAction.AllBooks)
            {
                foreach (var book in Books)
                {
                    Domain.Page? targetPage = null;
                    if (page.IsCover)
                    {
                        targetPage = book.Cover;
                    }
                    else
                    {
                        targetPage = book.Pages.FirstOrDefault(p => p.Index == page.Index);
                    }
                    
                    if (targetPage != null)
                    {
                        targetPage.SourcePath = filePath;
                        targetPage.IsLocked = true;
                        LoadThumbnailForPage(targetPage);
                        CopyDimensions(page, targetPage);
                    }
                }
                
                _ = FillSlotDimensionsAsync(page);
                UpdateExportCommands();
            }
            else if (action == DropAction.SelectedBooks && selectedBooks != null)
            {
                foreach (var book in selectedBooks)
                {
                    Domain.Page? targetPage = null;
                    if (page.IsCover)
                    {
                        targetPage = book.Cover;
                    }
                    else
                    {
                        targetPage = book.Pages.FirstOrDefault(p => p.Index == page.Index);
                    }
                    
                    if (targetPage != null)
                    {
                        targetPage.SourcePath = filePath;
                        LoadThumbnailForPage(targetPage);
                        CopyDimensions(page, targetPage);
                    }
                }
                
                _ = FillSlotDimensionsAsync(page);
                UpdateExportCommands();
            }
        }

        private async Task LoadThumbnailForPage(Page page)
        {
            if (string.IsNullOrEmpty(page.SourcePath)) return;
        }

        private async Task ExportAsync()
        {
            if (Project == null) return;

            // Combined mode must not silently skip empty slots the way Unique Folders
            // mode does, so the validation stays here - before the options modal opens.
            var missingSlots = new List<string>();
            foreach (var book in Books)
            {
                foreach (var p in book.Pages.Where(p => p.IsEmpty))
                    missingSlots.Add($"Книга {book.BookIndex}, разворот {p.Index}");
            }

            if (missingSlots.Count > 0)
            {
                var list = string.Join("\n", missingSlots.Take(12));
                if (missingSlots.Count > 12) list += $"\n... и ещё {missingSlots.Count - 12}";
                System.Windows.MessageBox.Show($"Не все развороты заполнены:\n\n{list}",
                    "Предупреждение", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            await RunExportAsync();
        }

        /// <summary>
        /// Ctrl+Shift+S. Validates covers as well, which the plain export does not, and
        /// otherwise shares the same options modal.
        /// </summary>
        private async Task ExportWithFolderAsync()
        {
            if (Project == null) return;

            var missingCovers = Books
                .Where(b => b.Cover == null || b.Cover.IsEmpty)
                .Select(b => $"Книга {b.BookIndex}: не назначена обложка")
                .ToList();

            var missingPages = new List<string>();
            foreach (var book in Books)
            {
                foreach (var p in book.Pages.Where(p => p.IsEmpty))
                    missingPages.Add($"Книга {book.BookIndex}, разворот {p.Index}");
            }

            if (missingCovers.Count > 0 || missingPages.Count > 0)
            {
                var parts = new List<string>();
                if (missingCovers.Count > 0)
                {
                    parts.Add("Не назначены обложки:\n" + string.Join("\n", missingCovers.Take(10)));
                }
                if (missingPages.Count > 0)
                {
                    var list = string.Join("\n", missingPages.Take(10));
                    if (missingPages.Count > 10) list += $"\n... и ещё {missingPages.Count - 10}";
                    parts.Add("Не заполнены развороты:\n" + list);
                }

                System.Windows.MessageBox.Show("Перед экспортом необходимо заполнить все обязательные поля:\n\n"
                                + string.Join("\n\n", parts),
                    "Предупреждение", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            await RunExportAsync();
        }

        /// <summary>
        /// Opens the options modal, which performs the copy, then updates the project
        /// status and shows the success dialog.
        /// </summary>
        private async Task RunExportAsync()
        {
            if (Project == null) return;

            var options = new Presentation.Dialogs.ExportDialog(Project, _exportService);
            options.Owner = System.Windows.Application.Current.MainWindow;

            IsLoading = true;
            try
            {
                if (options.ShowDialog() != true || !options.Exported)
                    return;

                string outputFolder = options.ExportedFolder;

                Project.OutputFolder = outputFolder;
                if (CurrentProjectInfo != null)
                {
                    CurrentProjectInfo.Status = ProjectStatus.SuccessfullyCompleted;
                    CurrentProjectInfo.PageCount = Books.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                    await _projectListService.SaveProjectInfoAsync(CurrentProjectInfo);
                }

                ErrorMessage = null;
                await ShowExportSuccessAsync(outputFolder);
                ProjectExported?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка экспорта: {ex.Message}";
                _loggingService.LogError("Ошибка экспорта", ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ShowExportSuccessAsync(string outputFolder)
        {
            var dialog = new Presentation.Dialogs.ExportSuccessDialog(outputFolder);
            dialog.Owner = System.Windows.Application.Current.MainWindow;
            if (dialog.ShowDialog() == true && dialog.GoToFolder)
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start("explorer.exe", outputFolder);
                    }
                    catch
                    {
                        // Explorer failing to open is not worth surfacing.
                    }
                });
            }
        }

        private void DeleteFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            AvailableFiles.Remove(filePath);
        }

        public async void SetProject(Project? project, ProjectInfo projectInfo)
        {
            Books.Clear();
            Project = null;
            AvailableFiles.Clear();
            IsStructureConfirmed = false;
            ErrorMessage = null;
            Presentation.Converters.PageSourceConverter.ClearCache();
            
            var projectId = projectInfo.Id ?? string.Empty;
            var projectName = projectInfo.Name ?? string.Empty;
            var projectFilePath = projectInfo.FilePath ?? string.Empty;
            var projectMode = projectInfo.Mode;
            
            if (string.IsNullOrEmpty(projectId))
            {
                projectId = Guid.NewGuid().ToString();
            }
            
            var projectInfoCopy = new ProjectInfo
            {
                Id = projectId,
                Name = projectName,
                FilePath = projectFilePath,
                Mode = projectMode,
                BookCount = projectInfo.BookCount,
                PageCount = projectInfo.PageCount,
                Status = projectInfo.Status,
                CreatedDate = projectInfo.CreatedDate,
                LastModified = projectInfo.LastModified
            };
            
            CurrentProjectInfo = projectInfoCopy;
            ProjectName = projectInfoCopy.Name;
            
            if (project == null && !string.IsNullOrEmpty(projectFilePath) && File.Exists(projectFilePath))
            {
                project = await _projectService.LoadProjectAsync(projectFilePath);
            }
            
            if (project == null)
            {
                project = new Project { Mode = AppMode.Combined };
            }
            else
            {
                project.Mode = AppMode.Combined;
                
                if (project.AvailableFiles != null)
                {
                    foreach (var file in project.AvailableFiles)
                    {
                        if (File.Exists(file))
                        {
                            AvailableFiles.Add(file);
                        }
                    }
                }
                
                if (project.Books != null && project.Books.Count > 0)
                {
                    NumberOfBooks = project.Books.Count;
                    SpreadsPerBook = project.Books.FirstOrDefault()?.Pages?.Count ?? 1;
                    IsStructureConfirmed = true;
                    
                    Books.Clear();
                    // Re-decide from what is on disk: the first spread of the saved run
                    // sets the shape again, so a reopened project looks exactly like it did
                    // when it was closed.
                            foreach (var book in project.Books)
                    {
                        Books.Add(book);
                    }
                    
                    foreach (var book in Books)
                    {
                        if (book.Cover != null && !string.IsNullOrEmpty(book.Cover.SourcePath))
                        {
                            var thumbDir = Path.Combine(Path.GetTempPath(), "PhotoBookRenamer", "Thumbnails");
                            var filePathHash = _imageService.GetFilePathHash(book.Cover.SourcePath);
                            var thumbName = $"{filePathHash}_thumb.jpg";
                            var thumbPath = Path.Combine(thumbDir, thumbName);
                            
                            if (File.Exists(thumbPath))
                            {
                                book.Cover.ThumbnailPath = thumbPath;
                            }
                        }
                        
                        foreach (var page in book.Pages.Where(p => !string.IsNullOrEmpty(p.SourcePath)))
                        {
                            var thumbDir = Path.Combine(Path.GetTempPath(), "PhotoBookRenamer", "Thumbnails");
                            var filePathHash = _imageService.GetFilePathHash(page.SourcePath);
                            var thumbName = $"{filePathHash}_thumb.jpg";
                            var thumbPath = Path.Combine(thumbDir, thumbName);
                            
                            if (File.Exists(thumbPath))
                            {
                                page.ThumbnailPath = thumbPath;
                            }
                        }
                    }
                }
            }
            
            Project = project;
            
            if (Project != null)
            {
                Project.Books.Clear();
                foreach (var book in Books)
                {
                    Project.Books.Add(book);
                }
                
                if (CurrentProjectInfo != null)
                {
                    CurrentProjectInfo.PageCount = Project.Books?.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                    CurrentProjectInfo.BookCount = Project.Books?.Count ?? 0;
                    _ = Task.Run(async () =>
                    {
                        await _projectListService.SaveProjectInfoAsync(CurrentProjectInfo);
                    });
                }
            }
            
            OnPropertyChanged(nameof(Books));
            OnPropertyChanged(nameof(Project));
            OnPropertyChanged(nameof(ProjectName));
            
            if (Project?.Books != null && Project.Books.Any())
            {
                var allImagePaths = Project.Books
                    .SelectMany(b => b.Pages.Select(p => p.SourcePath).Concat(new[] { b.Cover?.SourcePath }))
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();
                
                if (allImagePaths.Any())
                {
                    _ = Task.Run(async () =>
                    {
                        await _imageService.LoadThumbnailsAsync(allImagePaths!);
                        
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            foreach (var book in Project.Books)
                            {
                                if (book.Cover != null && !string.IsNullOrEmpty(book.Cover.SourcePath))
                                {
                                    var thumbDir = Path.Combine(Path.GetTempPath(), "PhotoBookRenamer", "Thumbnails");
                                    var filePathHash = _imageService.GetFilePathHash(book.Cover.SourcePath);
                                    var thumbName = $"{filePathHash}_thumb.jpg";
                                    var thumbPath = Path.Combine(thumbDir, thumbName);
                                    
                                    if (File.Exists(thumbPath) && book.Cover.ThumbnailPath != thumbPath)
                                    {
                                        book.Cover.ThumbnailPath = thumbPath;
                                    }
                                }
                                
                                foreach (var page in book.Pages.Where(p => !string.IsNullOrEmpty(p.SourcePath)))
                                {
                                    var thumbDir = Path.Combine(Path.GetTempPath(), "PhotoBookRenamer", "Thumbnails");
                                    var filePathHash = _imageService.GetFilePathHash(page.SourcePath);
                                    var thumbName = $"{filePathHash}_thumb.jpg";
                                    var thumbPath = Path.Combine(thumbDir, thumbName);
                                    
                                    if (File.Exists(thumbPath) && page.ThumbnailPath != thumbPath)
                                    {
                                        page.ThumbnailPath = thumbPath;
                                    }
                                }
                            }
                        });
                    });
                }
            }
            
            if (AvailableFiles.Any())
            {
                _ = Task.Run(async () => await _imageService.LoadThumbnailsAsync(AvailableFiles));
            }
            
            UpdateExportCommands();
        }

        private async Task SaveProjectAsync()
        {
            // The project record is created HERE rather than on the first "load photos"
            // click. It used to be created by the file loader only, and the save button's
            // CanExecute was CurrentProjectInfo != null - so a photo dropped straight into
            // an empty slot left the button dead, while the same photo loaded through the
            // loader panel worked. The user must not have to know which door the photo
            // came in by.
            if (await EnsureProjectInfoAsync() == null)
            {
                System.Windows.MessageBox.Show("Не удалось создать запись проекта.",
                    "Ошибка", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            // Инициализируем Project, если его нет
            if (Project == null)
            {
                Project = new Project { Mode = AppMode.Combined };
            }
            
            try
            {
                IsLoading = true;
                
                var projectId = CurrentProjectInfo.Id ?? string.Empty;
                if (string.IsNullOrEmpty(projectId))
                {
                    projectId = Guid.NewGuid().ToString();
                    if (CurrentProjectInfo != null)
                    {
                        CurrentProjectInfo.Id = projectId;
                    }
                }
                
                if (!string.IsNullOrEmpty(ProjectName) && ProjectName != CurrentProjectInfo.Name)
                {
                    CurrentProjectInfo.Name = ProjectName;
                }
                
                // Синхронизируем Books с Project.Books
                Project.Books.Clear();
                foreach (var book in Books)
                {
                    Project.Books.Add(book);
                }
                
                // Синхронизируем AvailableFiles
                Project.AvailableFiles.Clear();
                foreach (var file in AvailableFiles)
                {
                    Project.AvailableFiles.Add(file);
                }
                
                // Сохраняем проект
                var projectsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhotoBookRenamer",
                    "Projects");
                
                if (!Directory.Exists(projectsDir))
                {
                    Directory.CreateDirectory(projectsDir);
                }
                
                var filePath = Path.Combine(projectsDir, $"{projectId}.json");
                await _projectService.SaveProjectAsync(Project, filePath);
                
                if (!string.IsNullOrEmpty(ProjectName))
                {
                    CurrentProjectInfo.Name = ProjectName;
                }
                
                CurrentProjectInfo.FilePath = filePath;
                CurrentProjectInfo.BookCount = Books.Count;
                CurrentProjectInfo.PageCount = Books.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                CurrentProjectInfo.Status = DetermineStatus(Project);
                CurrentProjectInfo.LastModified = DateTime.Now;
                
                var updatedInfo = await _projectListService.UpdateProjectInfoAsync(Project, filePath);
                if (updatedInfo != null)
                {
                    var savedName = CurrentProjectInfo.Name;
                    CurrentProjectInfo.BookCount = updatedInfo.BookCount;
                    CurrentProjectInfo.PageCount = updatedInfo.PageCount;
                    CurrentProjectInfo.Status = updatedInfo.Status;
                    CurrentProjectInfo.LastModified = updatedInfo.LastModified;
                    CurrentProjectInfo.Name = savedName;
                }
                
                await _projectListService.SaveProjectInfoAsync(CurrentProjectInfo);
                CurrentMode = AppMode.ProjectList;
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Ошибка сохранения проекта", ex);
                System.Windows.MessageBox.Show($"Ошибка сохранения проекта: {ex.Message}", 
                    "Ошибка", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private ProjectStatus DetermineStatus(Project project)
        {
            if (project == null || project.Books == null || project.Books.Count == 0)
            {
                return ProjectStatus.NotFilled;
            }
            
            var allFilled = project.Books.All(book =>
                book.Cover != null && !book.Cover.IsEmpty &&
                book.Pages != null && book.Pages.All(p => !p.IsEmpty));
            
            return allFilled ? ProjectStatus.Ready : ProjectStatus.NotFilled;
        }

        private async Task BackAsync()
        {
            if (CurrentProjectInfo != null && Project != null && 
                (Books.Count > 0 || AvailableFiles.Count > 0))
            {
                var result = System.Windows.MessageBox.Show(
                    "Проект не сохранён. Сохранить перед выходом?",
                    "Подтверждение",
                    System.Windows.MessageBoxButton.YesNoCancel,
                    System.Windows.MessageBoxImage.Question);
                
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    await SaveProjectAsync();
                    return;
                }
                else if (result == System.Windows.MessageBoxResult.Cancel)
                {
                    return;
                }
            }
            
            if (CurrentProjectInfo != null && 
                (Books.Count == 0 && AvailableFiles.Count == 0))
            {
                try
                {
                    await _projectListService.DeleteProjectAsync(CurrentProjectInfo);
                }
                catch (Exception ex)
                {
                }
            }
            
            CurrentMode = AppMode.ProjectList;
        }

        private void DeleteBook(Book? book)
        {
            if (book == null) return;
            
            Books.Remove(book);
            
            for (int i = 0; i < Books.Count; i++)
            {
                Books[i].BookIndex = i + 1;
                Books[i].Name = $"Книга {i + 1}";
            }
            
            if (Project != null)
            {
                Project.Books.Clear();
                foreach (var b in Books)
                {
                    Project.Books.Add(b);
                }
            }
            
            UpdateExportCommands();
        }

        private void DuplicateBook(Book? book)
        {
            if (book == null) return;
            
            var newBook = new Book
            {
                BookIndex = Books.Count + 1,
                Name = $"Книга {Books.Count + 1}",
                Cover = new Page
                {
                    IsCover = true,
                    Index = 0,
                    SourcePath = book.Cover?.SourcePath,
                    ThumbnailPath = book.Cover?.ThumbnailPath,
                    // The copy shows the same file, so it carries the same size: a
                    // duplicated book keeps its frame instead of dropping to the default.
                    ImageWidth = book.Cover?.ImageWidth ?? 0,
                    ImageHeight = book.Cover?.ImageHeight ?? 0
                }
            };
            
            foreach (var page in book.Pages)
            {
                newBook.Pages.Add(new Page
                {
                    IsCover = false,
                    Index = page.Index,
                    DisplayIndex = page.DisplayIndex,
                    SourcePath = page.SourcePath,
                    ThumbnailPath = page.ThumbnailPath,
                    ImageWidth = page.ImageWidth,
                    ImageHeight = page.ImageHeight
                });
            }
            
            newBook.UpdatePageSlots();
            Books.Add(newBook);
            
            for (int i = 0; i < Books.Count; i++)
            {
                Books[i].BookIndex = i + 1;
                Books[i].Name = $"Книга {i + 1}";
            }
            
            NumberOfBooks = Books.Count;
            
            if (Project != null)
            {
                Project.Books.Clear();
                foreach (var b in Books)
                {
                    Project.Books.Add(b);
                }
            }
            
            UpdateExportCommands();
        }

        private void DeletePage(Page? page)
        {
            if (page == null) return;
            
            if (!string.IsNullOrEmpty(page.SourcePath))
            {
                Presentation.Converters.PageSourceConverter.ClearCacheForFile(page.SourcePath);
            }
            
            page.SourcePath = null;
            page.ThumbnailPath = null;
            page.FileName = null;
            
            var book = Books.FirstOrDefault(b => b.Cover == page || b.Pages.Contains(page));
            if (book != null)
            {
                book.UpdatePageSlots();
            }
            
            OnPropertyChanged(nameof(Books));
            UpdateExportCommands();
        }

        private async void LoadPageFile(Page? page)
        {
            if (page == null) return;
            
            try
            {
                var files = await _fileService.SelectFilesAsync();
                if (files == null || files.Length == 0) return;
                
                var file = files.FirstOrDefault(f => _fileService.IsJpegFile(f));
                if (file != null)
                {
                    if (!string.IsNullOrEmpty(page.SourcePath) && page.SourcePath != file)
                    {
                        Presentation.Converters.PageSourceConverter.ClearCacheForFile(page.SourcePath);
                    }
                    
                    Presentation.Converters.PageSourceConverter.ClearCacheForFile(file);
                    page.SourcePath = file;
                    
                    var book = Books.FirstOrDefault(b => b.Cover == page || b.Pages.Contains(page));
                    if (book != null)
                    {
                        book.UpdatePageSlots();
                    }

                    // Size first, then the frame: UpdatePageSlots is what re-shapes the
                    // book, so it has to run after the size is known.
                    await FillSlotDimensionsAsync(page);
                    
                    await LoadThumbnailForPage(page);
                    OnPropertyChanged(nameof(Books));
                    
                    if (!AvailableFiles.Contains(file))
                    {
                        AvailableFiles.Add(file);
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Ошибка загрузки файла для страницы", ex);
            }
        }

        private async void DuplicateToAllBooks(Page? page)
        {
            if (page == null || string.IsNullOrEmpty(page.SourcePath)) return;
            
            Presentation.Converters.PageSourceConverter.ClearCacheForFile(page.SourcePath);
            
            var tasks = new List<Task>();
            foreach (var book in Books)
            {
                Page? targetPage = null;
                if (page.IsCover)
                {
                    targetPage = book.Cover;
                }
                else
                {
                    targetPage = book.Pages.FirstOrDefault(p => p.Index == page.Index);
                }
                
                if (targetPage != null)
                {
                    if (!string.IsNullOrEmpty(targetPage.SourcePath) && targetPage.SourcePath != page.SourcePath)
                    {
                        Presentation.Converters.PageSourceConverter.ClearCacheForFile(targetPage.SourcePath);
                    }
                    
                    targetPage.SourcePath = page.SourcePath;
                    CopyDimensions(page, targetPage);
                    book.UpdatePageSlots();
                    tasks.Add(LoadThumbnailForPage(targetPage));
                }
            }
            
            await Task.WhenAll(tasks);
            OnPropertyChanged(nameof(Books));
            UpdateExportCommands();
        }

        private void ResetProject()
        {
            Project = null;
            Books.Clear();
            AvailableFiles.Clear();
            ErrorMessage = null;
            IsStructureConfirmed = false;
            CurrentProjectInfo = null;
            ProjectName = null;
        }
        
        private void DeletePageFromAllBooks(Page? page)
        {
            if (page == null) return;
            
            var firstBook = Books.FirstOrDefault();
            if (firstBook == null) return;
            
            var pagesWithoutCover = firstBook.Pages.Where(p => !p.IsCover).ToList();
            var pageIndex = pagesWithoutCover.IndexOf(page);
            if (pageIndex < 0) return;
            
            foreach (var book in Books)
            {
                var pages = book.Pages.Where(p => !p.IsCover).ToList();
                if (pageIndex < pages.Count)
                {
                    var targetPage = pages[pageIndex];
                    targetPage.SourcePath = null;
                    targetPage.ThumbnailPath = null;
                    targetPage.FileName = null;
                }
            }
            
            UpdateExportCommands();
        }
        
        private void AddBook()
        {
            var spreadsCount = Books.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? SpreadsPerBook;
            
            var newBookIndex = Books.Count + 1;
            var newBook = new Book
            {
                BookIndex = newBookIndex,
                Name = $"Книга {newBookIndex}",
                Cover = new Page { IsCover = true, Index = 0 }
            };
            
            for (int j = 0; j < spreadsCount; j++)
            {
                newBook.Pages.Add(new Page { IsCover = false, Index = j + 1, DisplayIndex = j + 1 });
            }
            
            newBook.UpdatePageSlots();
            Books.Add(newBook);
            
            if (Project != null)
            {
                Project.Books.Clear();
                foreach (var b in Books)
                {
                    Project.Books.Add(b);
                }
            }
            
            UpdateExportCommands();
        }
        
        private void AddSpread()
        {
            var firstBook = Books.FirstOrDefault();
            if (firstBook == null) return;
            
            var newIndex = firstBook.Pages.Count(p => !p.IsCover) + 1;
            
            foreach (var book in Books)
            {
                book.Pages.Add(new Page { IsCover = false, Index = newIndex, DisplayIndex = newIndex });
                book.UpdatePageSlots();
            }
            
        }
        
        private void MovePageLeft(Page? page)
        {
            if (page == null || page.IsCover) return;
            
            var book = Books.FirstOrDefault(b => b.Cover == page || b.Pages.Contains(page));
            if (book == null) return;
            
            var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
            var pageIndex = pagesWithoutCover.IndexOf(page);
            if (pageIndex <= 0) return;
            
            var currentIndex = book.Pages.IndexOf(page);
            var targetIndex = currentIndex - 1;
            
            if (targetIndex >= 0 && !book.Pages[targetIndex].IsCover)
            {
                book.Pages.Move(currentIndex, targetIndex);
                UpdatePageDisplayIndices(book);
            }
        }
        
        private void MovePageRight(Page? page)
        {
            if (page == null || page.IsCover) return;
            
            var book = Books.FirstOrDefault(b => b.Cover == page || b.Pages.Contains(page));
            if (book == null) return;
            
            var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
            var pageIndex = pagesWithoutCover.IndexOf(page);
            if (pageIndex >= pagesWithoutCover.Count - 1) return;
            
            var currentIndex = book.Pages.IndexOf(page);
            var targetIndex = currentIndex + 1;
            
            if (targetIndex < book.Pages.Count)
            {
                book.Pages.Move(currentIndex, targetIndex);
                UpdatePageDisplayIndices(book);
            }
        }
        
        private void UpdatePageDisplayIndices(Book book)
        {
            var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
            for (int i = 0; i < pagesWithoutCover.Count; i++)
            {
                pagesWithoutCover[i].DisplayIndex = i + 1;
                pagesWithoutCover[i].Index = i + 1;
            }
        }

        private void Undo()
        {
        }

        private void Redo()
        {
        }

        private async void OpenHelp()
        {
            if (CurrentProjectInfo != null && Project != null && Books != null && Books.Count > 0)
            {
                try
                {
                    await SaveProjectSilentlyAsync();
                }
                catch (Exception ex)
                {
                    _loggingService.LogError($"Ошибка при автоматическом сохранении проекта перед переходом в помощь: {ex.Message}", ex);
                }
            }
            
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var serviceProvider = ((App)System.Windows.Application.Current).GetServiceProvider();
                serviceProvider?.GetRequiredService<MainViewModel>()
                    .OpenHelp(HelpSection.Combined);
            });
        }
        
        private async Task SaveProjectSilentlyAsync()
        {
            if (CurrentProjectInfo == null)
            {
                return;
            }
            
            if (Project == null)
            {
                Project = new Project { Mode = AppMode.Combined };
            }
            
            try
            {
                var projectId = CurrentProjectInfo.Id ?? string.Empty;
                if (string.IsNullOrEmpty(projectId))
                {
                    projectId = Guid.NewGuid().ToString();
                    if (CurrentProjectInfo != null)
                    {
                        CurrentProjectInfo.Id = projectId;
                    }
                }
                
                if (!string.IsNullOrEmpty(ProjectName) && ProjectName != CurrentProjectInfo.Name)
                {
                    CurrentProjectInfo.Name = ProjectName;
                }
                
                // Синхронизируем Books с Project.Books
                Project.Books.Clear();
                foreach (var book in Books)
                {
                    Project.Books.Add(book);
                }
                
                // Синхронизируем AvailableFiles
                Project.AvailableFiles.Clear();
                foreach (var file in AvailableFiles)
                {
                    Project.AvailableFiles.Add(file);
                }
                
                // Сохраняем проект
                var projectsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhotoBookRenamer",
                    "Projects");
                
                if (!Directory.Exists(projectsDir))
                {
                    Directory.CreateDirectory(projectsDir);
                }
                
                var filePath = Path.Combine(projectsDir, $"{projectId}.json");
                await _projectService.SaveProjectAsync(Project, filePath);
                
                if (!string.IsNullOrEmpty(ProjectName))
                {
                    CurrentProjectInfo.Name = ProjectName;
                }
                
                CurrentProjectInfo.FilePath = filePath;
                CurrentProjectInfo.BookCount = Books.Count;
                CurrentProjectInfo.PageCount = Books.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                CurrentProjectInfo.Status = DetermineStatus(Project);
                CurrentProjectInfo.LastModified = DateTime.Now;
                
                await _projectListService.UpdateProjectInfoAsync(Project, filePath);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Ошибка автоматического сохранения проекта", ex);
            }
        }

        /// <summary>
        /// Creates the ProjectInfo record on demand. With direct tab navigation a user can
        /// land in this mode without ever having created a project, so the record is only
        /// written once there is something worth keeping.
        /// </summary>
        private async Task<ProjectInfo?> EnsureProjectInfoAsync()
        {
            if (CurrentProjectInfo != null)
                return CurrentProjectInfo;

            var name = string.IsNullOrWhiteSpace(ProjectName)
                ? $"Новый проект {DateTime.Now:yyyy-MM-dd HH:mm}"
                : ProjectName!;

            var info = await _projectListService.CreateProjectAsync(AppMode.Combined, name);
            if (info == null) return null;

            CurrentProjectInfo = info;
            OnPropertyChanged(nameof(CurrentProjectInfo));
            return info;
        }

        /// <summary>
        /// Silent save used by the header "Сохранить" button. Unlike SaveProjectAsync it
        /// does not navigate away, and unlike SaveProjectSilentlyAsync it creates the
        /// project record if none exists yet and surfaces failures to the user.
        /// </summary>
        public async Task<bool> QuickSaveAsync()
        {
            if (Books.Count == 0 && AvailableFiles.Count == 0)
                return false;

            if (await EnsureProjectInfoAsync() == null)
            {
                ErrorMessage = "Не удалось создать запись проекта.";
                OnPropertyChanged(nameof(ErrorMessage));
                return false;
            }

            await SaveProjectSilentlyAsync();
            return true;
        }

        /// <summary>
        /// Adds a single file that was dragged in from Windows Explorer.
        /// Returns false for unsupported formats or duplicates.
        ///
        /// The original drop handler was empty, so dragging photos from Explorer did
        /// nothing; the redesigned drop zone wires it up.
        /// </summary>
        public bool AddExternalFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
            if (AvailableFiles.Contains(filePath)) return false;
            if (!_fileService.IsJpegFile(filePath)) return false;

            AvailableFiles.Add(filePath);
            return true;
        }

        /// <summary>Kicks off thumbnail generation for pool items added outside the picker.</summary>
        public void LoadThumbnailsForNewFiles()
        {
            if (AvailableFiles.Count == 0) return;

            var paths = AvailableFiles.ToList();
            Task.Run(async () =>
            {
                await _imageService.LoadThumbnailsAsync(paths);
                Presentation.Converters.PageSourceConverter.ClearCache();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var p in Books)
                    {
                        if (p.Cover != null) p.Cover.RaiseThumbnailChanged();
                        foreach (var page in p.Pages) page.RaiseThumbnailChanged();
                    }
                });
            });
        }
    }

    public enum DropAction
    {
        ThisBookOnly,
        AllBooks,
        SelectedBooks
    }
}

