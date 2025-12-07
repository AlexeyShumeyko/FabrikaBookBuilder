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
using PhotoBookRenamer.Models;
using PhotoBookRenamer.Services;
using PhotoBookRenamer.Views;
using PhotoBookRenamer.Utils;

namespace PhotoBookRenamer.ViewModels
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
            UndoCommand = new RelayCommand(Undo, () => false); // Пока не используется в комбинированном режиме
            RedoCommand = new RelayCommand(Redo, () => false); // Пока не используется в комбинированном режиме
            
            // Обновляем команду экспорта при изменении проекта
            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Project))
                {
                    // Отписываемся от старого проекта
                    if (_project != null)
                    {
                        _project.PropertyChanged -= Project_PropertyChanged;
                    }
                    
                    // Подписываемся на новый проект
                    if (Project != null)
                    {
                        Project.PropertyChanged += Project_PropertyChanged;
                    }
                    
                    UpdateExportCommands();
                }
            };
            
            // Подписываемся на изменения Books для обновления команды экспорта
            Books.CollectionChanged += (s, e) =>
            {
                UpdateExportCommands();
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
                    if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
                    {
                        var serviceProvider = ((App)System.Windows.Application.Current).GetServiceProvider();
                        if (serviceProvider != null)
                        {
                            var mainVm = serviceProvider.GetRequiredService<MainViewModel>();
                            mainVm.CurrentMode = value;
                            mainWindow.DataContext = mainVm;
                        }
                    }
                });
            }
        }

        public ObservableCollection<string> AvailableFiles { get; }
        public ObservableCollection<Book> Books { get; }

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
            set => SetProperty(ref _projectName, value);
        }

        /// <summary>
        /// Сохраняет только название проекта без полного сохранения проекта и без переключения режима
        /// </summary>
        public async System.Threading.Tasks.Task SaveProjectNameOnlyAsync()
        {
            if (CurrentProjectInfo == null || string.IsNullOrEmpty(ProjectName))
            {
                return;
            }

            try
            {
                // Обновляем название в CurrentProjectInfo
                CurrentProjectInfo.Name = ProjectName;
                CurrentProjectInfo.LastModified = DateTime.Now;
                
                // Сохраняем только информацию о проекте (без полного сохранения проекта)
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
                    // КРИТИЧЕСКИ ВАЖНО: Если структура подтверждена, синхронизируем развороты во всех книгах
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

                // Добавляем новые файлы к существующим, избегая дубликатов
                foreach (var file in files)
                {
                    if (_fileService.IsJpegFile(file) && !AvailableFiles.Contains(file))
                    {
                        AvailableFiles.Add(file);
                    }
                }

                // Загружаем миниатюры в фоне
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
            // КРИТИЧЕСКИ ВАЖНО: Генерируем структуру только если она подтверждена
            if (!IsStructureConfirmed)
            {
                return;
            }
            
            // КРИТИЧЕСКИ ВАЖНО: Если книг нет - создаем все заново
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
                
                // КРИТИЧЕСКИ ВАЖНО: Принудительно обновляем UI после создания структуры
                OnPropertyChanged(nameof(Books));
            }
            else
            {
                // КРИТИЧЕСКИ ВАЖНО: Если книги есть - добавляем недостающие или удаляем лишние
                var currentCount = Books.Count;
                
                if (NumberOfBooks > currentCount)
                {
                    // Добавляем недостающие книги
                    for (int i = currentCount; i < NumberOfBooks; i++)
                    {
                        var book = new Book
                        {
                            BookIndex = i + 1,
                            Name = $"Книга {i + 1}",
                            Cover = new Page { IsCover = true, Index = 0 }
                        };

                        // Используем текущее количество разворотов из существующих книг
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
                    // Удаляем лишние книги (с конца)
                    while (Books.Count > NumberOfBooks)
                    {
                        Books.RemoveAt(Books.Count - 1);
                    }
                }
                
                // Синхронизируем количество разворотов во всех книгах
                SynchronizeSpreadsInAllBooks();
                
                // Обновляем индексы книг
                for (int i = 0; i < Books.Count; i++)
                {
                    Books[i].BookIndex = i + 1;
                    Books[i].Name = $"Книга {i + 1}";
                }
            }

            // Обновляем проект
            if (Project == null)
            {
                Project = new Project
                {
                    Mode = AppMode.Combined
                };
            }
            
            // Синхронизируем книги в проекте
            Project.Books.Clear();
            foreach (var book in Books)
            {
                Project.Books.Add(book);
            }
            
            // Обновляем команды экспорта после синхронизации
            UpdateExportCommands();
        }
        
        // КРИТИЧЕСКИ ВАЖНО: Синхронизирует количество разворотов во всех книгах
        private void SynchronizeSpreadsInAllBooks()
        {
            if (Books.Count == 0) return;
            
            
            foreach (var book in Books)
            {
                var currentSpreads = book.Pages.Count(p => !p.IsCover);
                
                if (currentSpreads < SpreadsPerBook)
                {
                    // Добавляем недостающие развороты
                    for (int i = currentSpreads; i < SpreadsPerBook; i++)
                    {
                        var newIndex = i + 1;
                        book.Pages.Add(new Page { IsCover = false, Index = newIndex, DisplayIndex = newIndex });
                    }
                }
                else if (currentSpreads > SpreadsPerBook)
                {
                    // Удаляем лишние развороты (с конца, но сохраняем те, у которых есть SourcePath)
                    var pagesToRemove = book.Pages.Where(p => !p.IsCover && p.Index > SpreadsPerBook).ToList();
                    foreach (var page in pagesToRemove)
                    {
                        // Удаляем только пустые развороты (без SourcePath)
                        if (string.IsNullOrEmpty(page.SourcePath))
                        {
                            book.Pages.Remove(page);
                        }
                    }
                    
                    // Если все еще больше, удаляем с конца (даже с SourcePath)
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
                
                // Обновляем индексы разворотов
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
            
            // Обновляем команду подтверждения
            if (ConfirmStructureCommand is RelayCommand confirmCmd)
            {
                confirmCmd.NotifyCanExecuteChanged();
            }
        }

        public void DropFileOnSlot(Models.Page page, string filePath, DropAction action, List<Book>? selectedBooks = null)
        {
            if (page == null || string.IsNullOrEmpty(filePath))
            {
                return;
            }

            if (action == DropAction.ThisBookOnly)
            {
                // КРИТИЧЕСКИ ВАЖНО: Одно фото должно иметь возможность быть применено к нескольким разворотам
                // НЕ очищаем файл из других страниц - это позволяет использовать одно фото в разных местах
                
                // Находим книгу для получения BookIndex
                var book = Books.FirstOrDefault(b => b.Cover == page || b.Pages.Contains(page));
                
                // Находим книгу для получения BookIndex
                var targetBook = Books.FirstOrDefault(b => b.Cover == page || b.Pages.Contains(page));
                
                // Очищаем кэш для старого файла (если был)
                if (!string.IsNullOrEmpty(page.SourcePath) && page.SourcePath != filePath)
                {
                    Utils.PageSourceConverter.ClearCacheForFile(page.SourcePath);
                }
                
                // Очищаем кэш для нового файла
                Utils.PageSourceConverter.ClearCacheForFile(filePath);
                
                // Устанавливаем SourcePath для этой страницы
                // КРИТИЧЕСКИ ВАЖНО: SetProperty в Page вызовет PropertyChanged,
                // который в Book.Page_PropertyChanged обновит AllSlotsPages
                page.SourcePath = filePath;
                
                // КРИТИЧЕСКИ ВАЖНО: Принудительно обновляем AllSlotsPages через обновление коллекции
                // Это заставит UI перерисовать все ячейки
                if (targetBook != null)
                {
                    targetBook.UpdatePageSlots();
                }
                
                // Принудительно обновляем коллекцию Books для обновления UI
                OnPropertyChanged(nameof(Books));
                
                // КРИТИЧЕСКИ ВАЖНО: Book.Page_PropertyChanged уже обновит AllSlotsPages
                // при изменении SourcePath через SetProperty
                
                // Загружаем миниатюру асинхронно в фоне, не блокируя UI
                LoadThumbnailForPage(page);
                
                // Обновляем команды экспорта после изменения страницы
                UpdateExportCommands();
            }
            else if (action == DropAction.AllBooks)
            {
                foreach (var book in Books)
                {
                    Models.Page? targetPage = null;
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
                
                // Обновляем команды экспорта
                UpdateExportCommands();
            }
            else if (action == DropAction.SelectedBooks && selectedBooks != null)
            {
                foreach (var book in selectedBooks)
                {
                    Models.Page? targetPage = null;
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
                
                // Обновляем команды экспорта
                UpdateExportCommands();
            }
        }

        private async Task LoadThumbnailForPage(Page page)
        {
            // КРИТИЧЕСКИ ВАЖНО: Для слотов используем оригинальные изображения напрямую
            // Миниатюры не нужны - они создаются только для списка файлов
            // Это значительно ускоряет работу и улучшает качество
            if (string.IsNullOrEmpty(page.SourcePath)) return;
            
            // Просто устанавливаем SourcePath - UI обновится автоматически через SetProperty
            // Оригинальное изображение будет загружено с оптимизацией через DecodePixelWidth/Height
        }

        private async Task ExportAsync()
        {
            if (Project == null) return;

            // Валидация
            var missingSlots = Books.SelectMany((book, idx) =>
                book.Pages.Where(p => p.IsEmpty).Select(p => $"Книга {idx + 1}, разворот {p.Index}")
            ).ToList();

            if (missingSlots.Any())
            {
                var message = "Не все развороты заполнены:\n" + string.Join("\n", missingSlots);
                System.Windows.MessageBox.Show(message, "Предупреждение", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var outputFolder = await _fileService.SelectOutputFolderWithNameAsync(
                defaultPath: Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                defaultFolderName: "PhotoBookExport");
            
            if (string.IsNullOrEmpty(outputFolder))
                return;

            IsLoading = true;
            try
            {
                var success = await _exportService.ExportProjectAsync(Project, outputFolder);
                if (success)
                {
                    ErrorMessage = null;
                    
                    // Обновляем статус проекта после успешного экспорта
                    if (CurrentProjectInfo != null)
                    {
                        CurrentProjectInfo.Status = ProjectStatus.SuccessfullyCompleted;
                        // КРИТИЧЕСКИ ВАЖНО: PageCount - это количество разворотов в одной книге, а не сумма по всем книгам
                        CurrentProjectInfo.PageCount = Project.Books?.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                        Project.OutputFolder = outputFolder;
                        await _projectListService.SaveProjectInfoAsync(CurrentProjectInfo);
                    }
                    
                    var dialog = new Utils.ExportSuccessDialog(outputFolder);
                    dialog.Owner = System.Windows.Application.Current.MainWindow;
                    if (dialog.ShowDialog() == true && dialog.GoToFolder)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start("explorer.exe", outputFolder);
                        }
                        catch
                        {
                            // Игнорируем ошибки открытия папки
                        }
                    }
                }
                else
                {
                    ErrorMessage = "Ошибка при экспорте";
                }
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

        private async Task ExportWithFolderAsync()
        {
            if (Project == null) return;

            // Валидация (та же логика, что и в ExportAsync)
            var missingCovers = Books.Where((book, idx) => book.Cover == null || book.Cover.IsEmpty)
                .Select((book, idx) => $"Книга {idx + 1}").ToList();

            var missingPages = Books.SelectMany((book, idx) =>
                book.Pages.Where(p => p.IsEmpty).Select(p => $"Книга {idx + 1}, разворот {p.Index}")
            ).ToList();

            var errors = new List<string>();
            if (missingCovers.Any())
            {
                errors.Add("Не заполнены обложки:\n" + string.Join("\n", missingCovers));
            }
            if (missingPages.Any())
            {
                errors.Add("Не заполнены развороты:\n" + string.Join("\n", missingPages.Take(10)));
                if (missingPages.Count > 10)
                {
                    errors[errors.Count - 1] += $"\n... и еще {missingPages.Count - 10}";
                }
            }

            if (errors.Any())
            {
                var message = "Перед экспортом необходимо заполнить все обязательные поля:\n\n" + string.Join("\n\n", errors);
                System.Windows.MessageBox.Show(message, "Предупреждение", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var outputFolder = await _fileService.SelectOutputFolderAsync(
                defaultPath: Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            
            if (string.IsNullOrEmpty(outputFolder))
                return;

            IsLoading = true;
            try
            {
                var success = await _exportService.ExportProjectAsync(Project, outputFolder);
                if (success)
                {
                    ErrorMessage = null;
                    
                    // Обновляем статус проекта после успешного экспорта
                    if (CurrentProjectInfo != null)
                    {
                        CurrentProjectInfo.Status = ProjectStatus.SuccessfullyCompleted;
                        // КРИТИЧЕСКИ ВАЖНО: PageCount - это количество разворотов в одной книге, а не сумма по всем книгам
                        CurrentProjectInfo.PageCount = Project.Books?.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                        Project.OutputFolder = outputFolder;
                        await _projectListService.SaveProjectInfoAsync(CurrentProjectInfo);
                    }
                    
                    var dialog = new Utils.ExportSuccessDialog(outputFolder);
                    dialog.Owner = System.Windows.Application.Current.MainWindow;
                    if (dialog.ShowDialog() == true && dialog.GoToFolder)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start("explorer.exe", outputFolder);
                        }
                        catch
                        {
                            // Игнорируем ошибки открытия папки
                        }
                    }
                }
                else
                {
                    ErrorMessage = "Ошибка при экспорте";
                }
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

        private void DeleteFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            AvailableFiles.Remove(filePath);
        }

        public async void SetProject(Project? project, ProjectInfo projectInfo)
        {
            // Очищаем предыдущее состояние
            Books.Clear();
            Project = null;
            AvailableFiles.Clear();
            IsStructureConfirmed = false;
            ErrorMessage = null;
            // КРИТИЧЕСКИ ВАЖНО: Очищаем кэш изображений при смене проекта для предотвращения утечек памяти
            Utils.PageSourceConverter.ClearCache();
            
            // Сохраняем данные проекта в локальные переменные
            var projectId = projectInfo.Id ?? string.Empty;
            var projectName = projectInfo.Name ?? string.Empty;
            var projectFilePath = projectInfo.FilePath ?? string.Empty;
            var projectMode = projectInfo.Mode;
            
            if (string.IsNullOrEmpty(projectId))
            {
                projectId = Guid.NewGuid().ToString();
            }
            
            // Создаём копию projectInfo
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
            
            // Загружаем проект, если он существует
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
                
                // Загружаем доступные файлы из проекта
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
                
                // Восстанавливаем структуру из загруженного проекта
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
                    
                    // КРИТИЧЕСКИ ВАЖНО: Устанавливаем ThumbnailPath для всех страниц и обложек, если миниатюры уже существуют
                    // Это позволяет UI использовать миниатюры сразу, без ожидания их создания
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
            
            // Если структура не была восстановлена, генерируем новую
            if (!IsStructureConfirmed)
            {
            }
            
            // Синхронизируем Books с Project.Books
            if (Project != null)
            {
                Project.Books.Clear();
                foreach (var book in Books)
                {
                    Project.Books.Add(book);
                }
                
                // КРИТИЧЕСКИ ВАЖНО: Обновляем PageCount на основе реальных данных проекта
                if (CurrentProjectInfo != null)
                {
                    // КРИТИЧЕСКИ ВАЖНО: PageCount - это количество разворотов в одной книге, а не сумма по всем книгам
                    CurrentProjectInfo.PageCount = Project.Books?.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                    CurrentProjectInfo.BookCount = Project.Books?.Count ?? 0;
                    // Сохраняем обновлённую информацию в фоне
                    _ = Task.Run(async () =>
                    {
                        await _projectListService.SaveProjectInfoAsync(CurrentProjectInfo);
                    });
                }
            }
            
            OnPropertyChanged(nameof(Books));
            OnPropertyChanged(nameof(Project));
            OnPropertyChanged(nameof(ProjectName));
            
            // КРИТИЧЕСКИ ВАЖНО: Запускаем фоновую загрузку миниатюр для всех изображений в проекте
            // Это значительно ускоряет отображение UI после загрузки проекта
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
                        
                        // Обновляем ThumbnailPath для всех страниц и обложек после загрузки миниатюр
                        // Используем Dispatcher для обновления UI на правильном потоке
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
            
            // Запускаем фоновую загрузку миниатюр для всех доступных файлов
            if (AvailableFiles.Any())
            {
                _ = Task.Run(async () => await _imageService.LoadThumbnailsAsync(AvailableFiles));
            }
            
            // Обновляем команды экспорта
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
                
                
                // Обновляем название проекта, если оно было изменено
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
                
                // КРИТИЧЕСКИ ВАЖНО: Обновляем название проекта в CurrentProjectInfo перед сохранением
                if (!string.IsNullOrEmpty(ProjectName))
                {
                    CurrentProjectInfo.Name = ProjectName;
                }
                
                // Обновляем информацию о проекте
                CurrentProjectInfo.FilePath = filePath;
                CurrentProjectInfo.BookCount = Books.Count;
                // КРИТИЧЕСКИ ВАЖНО: PageCount - это количество разворотов в одной книге, а не сумма по всем книгам
                CurrentProjectInfo.PageCount = Books.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                CurrentProjectInfo.Status = DetermineStatus(Project);
                CurrentProjectInfo.LastModified = DateTime.Now;
                
                // Обновляем информацию о проекте в списке
                var updatedInfo = await _projectListService.UpdateProjectInfoAsync(Project, filePath);
                if (updatedInfo != null)
                {
                    // Синхронизируем с обновлённой информацией, но сохраняем пользовательское название
                    var savedName = CurrentProjectInfo.Name; // Сохраняем название перед обновлением
                    CurrentProjectInfo.BookCount = updatedInfo.BookCount;
                    CurrentProjectInfo.PageCount = updatedInfo.PageCount;
                    CurrentProjectInfo.Status = updatedInfo.Status;
                    CurrentProjectInfo.LastModified = updatedInfo.LastModified;
                    CurrentProjectInfo.Name = savedName; // Восстанавливаем пользовательское название
                }
                
                // КРИТИЧЕСКИ ВАЖНО: Сохраняем обновлённую информацию о проекте с правильным названием
                await _projectListService.SaveProjectInfoAsync(CurrentProjectInfo);
                
                
                // Возвращаемся на список проектов
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
            // Если проект не сохранён и не пуст, спрашиваем о сохранении
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
            
            // Если проект пуст и не сохранён, удаляем его
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
            
            // Обновляем индексы книг
            for (int i = 0; i < Books.Count; i++)
            {
                Books[i].BookIndex = i + 1;
                Books[i].Name = $"Книга {i + 1}";
            }
            
            // Синхронизируем с Project
            if (Project != null)
            {
                Project.Books.Clear();
                foreach (var b in Books)
                {
                    Project.Books.Add(b);
                }
            }
            
            // Обновляем команды экспорта
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
            
            // Обновляем индексы книг
            for (int i = 0; i < Books.Count; i++)
            {
                Books[i].BookIndex = i + 1;
                Books[i].Name = $"Книга {i + 1}";
            }
            
            // Обновляем счётчик количества книг
            NumberOfBooks = Books.Count;
            
            // Синхронизируем с Project
            if (Project != null)
            {
                Project.Books.Clear();
                foreach (var b in Books)
                {
                    Project.Books.Add(b);
                }
            }
            
            // Обновляем команды экспорта
            UpdateExportCommands();
        }

        private void DeletePage(Page? page)
        {
            if (page == null) return;
            
            // Очищаем кэш для удаляемого файла
            if (!string.IsNullOrEmpty(page.SourcePath))
            {
                Utils.PageSourceConverter.ClearCacheForFile(page.SourcePath);
            }
            
            page.SourcePath = null;
            page.ThumbnailPath = null;
            page.FileName = null;
            
            // КРИТИЧЕСКИ ВАЖНО: Обновляем AllSlotsPages для немедленного обновления UI
            var book = Books.FirstOrDefault(b => b.Cover == page || b.Pages.Contains(page));
            if (book != null)
            {
                book.UpdatePageSlots();
            }
            
            // Принудительно обновляем коллекцию Books для обновления UI
            OnPropertyChanged(nameof(Books));
            
            // Обновляем команды экспорта
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
                    // Очищаем кэш для старого файла (если был)
                    if (!string.IsNullOrEmpty(page.SourcePath) && page.SourcePath != file)
                    {
                        Utils.PageSourceConverter.ClearCacheForFile(page.SourcePath);
                    }
                    
                    // Очищаем кэш для нового файла
                    Utils.PageSourceConverter.ClearCacheForFile(file);
                    
                    // Применяем файл напрямую к странице (как при перетаскивании)
                    page.SourcePath = file;
                    
                    // КРИТИЧЕСКИ ВАЖНО: Обновляем AllSlotsPages для немедленного обновления UI
                    var book = Books.FirstOrDefault(b => b.Cover == page || b.Pages.Contains(page));
                    if (book != null)
                    {
                        book.UpdatePageSlots();
                    }
                    
                    await LoadThumbnailForPage(page);
                    
                    // Принудительно обновляем коллекцию Books для обновления UI
                    OnPropertyChanged(nameof(Books));
                    
                    // Если файл ещё не в списке доступных, добавляем его
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
            
            // Очищаем кэш для файла, который будет дублироваться
            Utils.PageSourceConverter.ClearCacheForFile(page.SourcePath);
            
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
                    // Очищаем кэш для старого файла (если был)
                    if (!string.IsNullOrEmpty(targetPage.SourcePath) && targetPage.SourcePath != page.SourcePath)
                    {
                        Utils.PageSourceConverter.ClearCacheForFile(targetPage.SourcePath);
                    }
                    
                    // Устанавливаем SourcePath
                    targetPage.SourcePath = page.SourcePath;
                    
                    // КРИТИЧЕСКИ ВАЖНО: Обновляем AllSlotsPages для немедленного обновления UI
                    book.UpdatePageSlots();
                    
                    tasks.Add(LoadThumbnailForPage(targetPage));
                }
            }
            
            await Task.WhenAll(tasks);
            
            // Принудительно обновляем коллекцию Books для обновления UI
            OnPropertyChanged(nameof(Books));
            
            // Обновляем команды экспорта
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
            
            
            // Находим индекс страницы в первой книге
            var firstBook = Books.FirstOrDefault();
            if (firstBook == null) return;
            
            var pagesWithoutCover = firstBook.Pages.Where(p => !p.IsCover).ToList();
            var pageIndex = pagesWithoutCover.IndexOf(page);
            if (pageIndex < 0) return;
            
            // Удаляем фото из соответствующей страницы во всех книгах
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
            
            // Обновляем команды экспорта
            UpdateExportCommands();
        }
        
        private void AddBook()
        {
            
            // Определяем количество разворотов из существующих книг
            var spreadsCount = Books.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? SpreadsPerBook;
            
            var newBookIndex = Books.Count + 1;
            var newBook = new Book
            {
                BookIndex = newBookIndex,
                Name = $"Книга {newBookIndex}",
                Cover = new Page { IsCover = true, Index = 0 }
            };
            
            // Добавляем такое же количество разворотов, как в других книгах
            for (int j = 0; j < spreadsCount; j++)
            {
                newBook.Pages.Add(new Page { IsCover = false, Index = j + 1, DisplayIndex = j + 1 });
            }
            
            newBook.UpdatePageSlots();
            Books.Add(newBook);
            
            // Синхронизируем с Project
            if (Project != null)
            {
                Project.Books.Clear();
                foreach (var b in Books)
                {
                    Project.Books.Add(b);
                }
            }
            
            // Обновляем команды экспорта
            UpdateExportCommands();
        }
        
        private void AddSpread()
        {
            
            // Определяем новый индекс разворота
            var firstBook = Books.FirstOrDefault();
            if (firstBook == null) return;
            
            var newIndex = firstBook.Pages.Count(p => !p.IsCover) + 1;
            
            // Добавляем разворот во все книги
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
            if (pageIndex <= 0) return; // Уже на первой позиции
            
            // Перемещаем страницу в коллекции (влево - на позицию раньше)
            var currentIndex = book.Pages.IndexOf(page);
            var targetIndex = currentIndex - 1;
            
            // Убеждаемся, что не перемещаем на позицию обложки
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
            if (pageIndex >= pagesWithoutCover.Count - 1) return; // Уже на последней позиции
            
            // Перемещаем страницу в коллекции (вправо - на позицию позже)
            var currentIndex = book.Pages.IndexOf(page);
            var targetIndex = currentIndex + 1;
            
            // Убеждаемся, что не выходим за границы
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
            // TODO: Реализовать для комбинированного режима
        }

        private void Redo()
        {
            // TODO: Реализовать для комбинированного режима
        }

        private async void OpenHelp()
        {
            // Автоматически сохраняем проект перед переходом в помощь
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
                if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
                {
                    var serviceProvider = ((App)System.Windows.Application.Current).GetServiceProvider();
                    if (serviceProvider != null)
                    {
                        var mainVm = serviceProvider.GetRequiredService<MainViewModel>();
                        mainVm.OpenHelp(HelpSection.Combined);
                        mainWindow.DataContext = mainVm;
                    }
                }
            });
        }
        
        private async Task SaveProjectSilentlyAsync()
        {
            if (CurrentProjectInfo == null)
            {
                return;
            }
            
            // Инициализируем Project, если его нет
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
                
                // Обновляем название проекта, если оно было изменено
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
                
                // Обновляем название проекта в CurrentProjectInfo перед сохранением
                if (!string.IsNullOrEmpty(ProjectName))
                {
                    CurrentProjectInfo.Name = ProjectName;
                }
                
                // Обновляем информацию о проекте
                CurrentProjectInfo.FilePath = filePath;
                CurrentProjectInfo.BookCount = Books.Count;
                CurrentProjectInfo.PageCount = Books.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                CurrentProjectInfo.Status = DetermineStatus(Project);
                CurrentProjectInfo.LastModified = DateTime.Now;
                
                // Обновляем информацию о проекте в списке
                await _projectListService.UpdateProjectInfoAsync(Project, filePath);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Ошибка автоматического сохранения проекта", ex);
                // Не показываем ошибку пользователю при автоматическом сохранении
            }
        }
    }

    public enum DropAction
    {
        ThisBookOnly,
        AllBooks,
        SelectedBooks
    }
}

