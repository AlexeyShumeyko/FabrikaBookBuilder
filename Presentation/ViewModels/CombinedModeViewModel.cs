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
            
            LoadFilesCommand = new AsyncRelayCommand(LoadFilesAsync);
            ClearFilesCommand = new RelayCommand(ClearFiles);
            GenerateStructureCommand = new RelayCommand(GenerateStructure);
            ConfirmStructureCommand = new RelayCommand(ConfirmStructure, () => NumberOfBooks > 0 && SpreadsPerBook > 0);
            ExportCommand = new AsyncRelayCommand(ExportAsync, () => Project?.IsValid ?? false);
            ExportWithFolderCommand = new AsyncRelayCommand(ExportWithFolderAsync, () => Project?.IsValid ?? false);
            SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync, () => CurrentProjectInfo != null);
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
                OnPropertyChanged(nameof(BooksCount));
                OnPropertyChanged(nameof(HasStructure));
                OnPropertyChanged(nameof(CurrentBookProgress));
                OnPropertyChanged(nameof(SelectedBookProgress));
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
            };
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
            IsLoading = true;
            ErrorMessage = null;

            try
            {
                var files = await _fileService.SelectFilesAsync();
                
                if (files == null || files.Length == 0)
                {
                    IsLoading = false;
                    return;
                }

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
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка: {ex.Message}";
                _loggingService.LogError("Ошибка загрузки файлов", ex);
            }
            finally
            {
                IsLoading = false;
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

                        var spreadsCount = Books.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? SpreadsPerBook;
                        for (int j = 0; j < spreadsCount; j++)
                        {
                            book.Pages.Add(new Page { IsCover = false, Index = j + 1, DisplayIndex = j + 1 });
                        }

                        book.UpdatePageSlots();
                        Books.Add(book);
                    }
                }
                else if (NumberOfBooks < currentCount)
                {
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
                    }
                }
                
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
                    }
                }
                
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
            if (CurrentProjectInfo == null)
            {
                System.Windows.MessageBox.Show("Информация о проекте не найдена. Пожалуйста, создайте новый проект.", 
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
                    ThumbnailPath = book.Cover?.ThumbnailPath
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
                    ThumbnailPath = page.ThumbnailPath
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

