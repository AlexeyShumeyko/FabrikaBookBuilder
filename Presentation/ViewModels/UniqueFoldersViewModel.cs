using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoBookRenamer.Domain;
using PhotoBookRenamer.Application;
using PhotoBookRenamer.Infrastructure;
using PhotoBookRenamer.Presentation.Views;

namespace PhotoBookRenamer.Presentation.ViewModels
{
    public class UniqueFoldersViewModel : ViewModelBase
    {
        private readonly IFileService _fileService;
        private readonly IImageService _imageService;
        private readonly IProjectService _projectService;
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

        /// <summary>"1 обложка + 5 разворотов" style summary of the whole project.</summary>
        public string BooksSummary
        {
            get
            {
                if (Books.Count == 0) return "ничего не загружено";

                int covers = 0, spreads = 0;
                foreach (var b in Books)
                {
                    if (b.Cover != null && !b.Cover.IsEmpty) covers++;
                    if (b.Pages != null)
                        foreach (var p in b.Pages)
                            if (p != null && !p.IsCover && !p.IsEmpty) spreads++;
                }

                if (spreads == 0) return $"{covers} обложек";
                return $"{covers} обложек · {spreads} разворотов";
            }
        }

        public ProjectInfo? CurrentProjectInfo
        {
            get => _currentProjectInfo;
            private set
            {
                if (SetProperty(ref _currentProjectInfo, value))
                {
                    // Обновляем команду сохранения при изменении CurrentProjectInfo
                    if (SaveProjectCommand is AsyncRelayCommand saveCmd)
                    {
                        saveCmd.NotifyCanExecuteChanged();
                    }
                }
            }
        }

        public UniqueFoldersViewModel(
            IFileService fileService,
            IImageService imageService,
            IProjectService projectService,
            IExportService exportService,
            ILoggingService loggingService,
            IProjectListService projectListService)
        {
            _fileService = fileService;
            _imageService = imageService;
            _projectService = projectService;
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
                // При выходе назад проверяем, нужно ли удалить пустой проект
                if (CurrentProjectInfo != null)
                {
                    // Проверяем, был ли проект сохранён в файл
                    var wasSaved = !string.IsNullOrEmpty(CurrentProjectInfo.FilePath) && 
                                   System.IO.File.Exists(CurrentProjectInfo.FilePath);
                    
                    // Если проект не был сохранён в файл и пустой, удаляем его из списка
                    // Это предотвращает создание пустых проектов в списке
                    if (!wasSaved && (Project == null || Project.Books == null || Project.Books.Count == 0))
                    {
                        await _projectListService.DeleteProjectAsync(CurrentProjectInfo);
                    }
                }
                
                // Возвращаемся на главную страницу со списком проектов
                CurrentMode = AppMode.ProjectList;
            });
            SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync, () => CurrentProjectInfo != null);
            DeleteBookCommand = new RelayCommand<Book>(DeleteBook);
            ResetProjectCommand = new RelayCommand(ResetProject);
            UndoCommand = new RelayCommand(Undo, () => _projectService.CanUndo);
            RedoCommand = new RelayCommand(Redo, () => _projectService.CanRedo);
            MovePageUpCommand = new RelayCommand<Page>(MovePageUp);
            MovePageDownCommand = new RelayCommand<Page>(MovePageDown);
            AssignPageNumberCommand = new RelayCommand<Page>(AssignPageNumber);
            AssignCoverCommand = new RelayCommand<Page>(AssignCover);
            ResetOrderCommand = new RelayCommand(ResetOrder);
            OpenHelpCommand = new RelayCommand(OpenHelp);

            // WPF only re-evaluates a binding when the SOURCE raises PropertyChanged.
            // An ObservableCollection growing is not enough, so counts are surfaced
            // explicitly for the empty state and the toolbar summary.
            Books.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(BooksCount));
                OnPropertyChanged(nameof(BooksSummary));
            };

            // Обновляем команды при изменении проекта
            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Project))
                {
                    OnPropertyChanged(nameof(CanExport));
                    OnPropertyChanged(nameof(BooksSummary));
                    if (ExportCommand is AsyncRelayCommand exportCmd)
                        exportCmd.NotifyCanExecuteChanged();
                    if (ExportWithFolderCommand is AsyncRelayCommand exportFolderCmd)
                        exportFolderCmd.NotifyCanExecuteChanged();
                }
                if (e.PropertyName == nameof(CurrentProjectInfo) && SaveProjectCommand is AsyncRelayCommand saveCmd)
                {
                    saveCmd.NotifyCanExecuteChanged();
                }
            };
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
        
        public async void SetProject(Project? project, ProjectInfo projectInfo)
        {
            // КРИТИЧЕСКИ ВАЖНО: Сначала очищаем предыдущее состояние проекта
            // Это гарантирует, что при открытии нового проекта не останется данных от предыдущего
            Books.Clear();
            Project = null;
            // НЕ сбрасываем CurrentProjectInfo здесь - он будет установлен ниже
            ProjectName = null;
            ErrorMessage = null;
            _projectService.ClearHistory();
            // КРИТИЧЕСКИ ВАЖНО: Очищаем кэш изображений при смене проекта для предотвращения утечек памяти
            Presentation.Converters.PageSourceConverter.ClearCache();
            
            // КРИТИЧЕСКИ ВАЖНО: Сохраняем ВСЕ данные проекта в локальные переменные СРАЗУ
            // Это гарантирует, что мы используем правильные данные для этого проекта
            var projectId = projectInfo.Id ?? string.Empty;
            var projectName = projectInfo.Name ?? string.Empty;
            var projectFilePath = projectInfo.FilePath ?? string.Empty;
            var projectMode = projectInfo.Mode;
            var projectBookCount = projectInfo.BookCount;
            var projectStatus = projectInfo.Status;
            var projectCreatedDate = projectInfo.CreatedDate;
            var projectLastModified = projectInfo.LastModified;
            
            if (string.IsNullOrEmpty(projectId))
            {
                projectId = Guid.NewGuid().ToString();
            }
            
            // КРИТИЧЕСКИ ВАЖНО: Создаём НОВЫЙ projectInfo с правильными данными из локальных переменных
            // Это гарантирует, что CurrentProjectInfo имеет правильный Id и не будет изменён
            var projectInfoCopy = new ProjectInfo
            {
                Id = projectId, // ВСЕГДА используем ID из локальной переменной
                Name = projectName,
                FilePath = projectFilePath,
                Mode = projectMode,
                BookCount = projectBookCount,
                Status = projectStatus,
                CreatedDate = projectCreatedDate,
                LastModified = projectLastModified
            };
            
            // Сохраняем ссылку на копию projectInfo
            CurrentProjectInfo = projectInfoCopy;
            ProjectName = projectInfoCopy.Name;
            
            // Устанавливаем проект
            if (project == null)
            {
                project = new Project { Mode = projectInfo.Mode };
            }
            else
            {
                // Убеждаемся, что режим правильный
                project.Mode = projectInfo.Mode;
                
                // КРИТИЧЕСКИ ВАЖНО: После десериализации нужно инициализировать AllSlots для каждой книги
                if (project.Books != null && project.Books.Count > 0)
                {
                    foreach (var book in project.Books)
                    {
                        // Убеждаемся, что Pages инициализирована (должна быть после десериализации)
                        if (book.Pages != null)
                        {
                            // Обновляем слоты страниц после десериализации
                            book.UpdatePageSlots();
                        }
                    }
                }
            }
            
            // КРИТИЧЕСКИ ВАЖНО: Сначала синхронизируем Books с Project.Books ДО установки Project
            // Это гарантирует, что книги не будут потеряны при установке Project
            Books.Clear();
            if (project != null && project.Books != null)
            {
                // Добавляем ВСЕ книги в коллекцию для отображения (даже если их 0)
                foreach (var book in project.Books)
                {
                    Books.Add(book);
                }
            }
            
            // Устанавливаем проект ПОСЛЕ синхронизации Books
            Project = project;
            
            // КРИТИЧЕСКИ ВАЖНО: Дополнительно убеждаемся, что Books синхронизированы с Project.Books
            // Это нужно на случай, если setter Project очистил Books
            if (Project != null && Project.Books != null && Books.Count != Project.Books.Count)
            {
                Books.Clear();
                foreach (var book in Project.Books)
                {
                    Books.Add(book);
                }
            }
            
            // КРИТИЧЕСКИ ВАЖНО: Обновляем PageCount на основе реальных данных проекта
            if (Project != null && CurrentProjectInfo != null)
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
            
            // Уведомляем UI об обновлении
            OnPropertyChanged(nameof(Books));
            OnPropertyChanged(nameof(Project));
            OnPropertyChanged(nameof(ProjectName));
            
            // Обновляем команду сохранения после установки проекта
            if (SaveProjectCommand is AsyncRelayCommand saveCmd)
            {
                saveCmd.NotifyCanExecuteChanged();
            }
            
            // КРИТИЧЕСКИ ВАЖНО: Сначала устанавливаем ThumbnailPath для уже существующих миниатюр
            // Это позволяет UI использовать миниатюры сразу, без ожидания их создания
            if (Project != null && Project.Books != null && Project.Books.Count > 0)
            {
                foreach (var book in Project.Books)
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
                
                // Загружаем недостающие миниатюры в фоне
                var allImagePaths = Project.Books
                    .SelectMany(b => b.Pages.Select(p => p.SourcePath).Concat(new[] { b.Cover?.SourcePath }))
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();
                
                // Загружаем миниатюры в фоне, чтобы не блокировать UI
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
                Project = new Project { Mode = CurrentProjectInfo.Mode };
            }
            
            try
            {
                IsLoading = true;
                
                // КРИТИЧЕСКИ ВАЖНО: Сохраняем Id проекта в локальную переменную СРАЗУ
                // Это гарантирует, что мы используем правильный Id для сохранения
                var projectId = CurrentProjectInfo?.Id ?? string.Empty;
                if (string.IsNullOrEmpty(projectId))
                {
                    projectId = Guid.NewGuid().ToString();
                    if (CurrentProjectInfo != null)
                    {
                        CurrentProjectInfo.Id = projectId;
                    }
                }
                
                // КРИТИЧЕСКИ ВАЖНО: Формируем путь на основе Id проекта
                // В новой системе файлы хранятся прямо в папке Projects, формат: {ProjectId}.json
                var projectsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhotoBookRenamer",
                    "Projects");
                
                if (!Directory.Exists(projectsDir))
                {
                    Directory.CreateDirectory(projectsDir);
                }
                
                // ВСЕГДА формируем путь на основе Id из локальной переменной
                var projectFilePath = Path.Combine(projectsDir, $"{projectId}.json");
                
                // Сохраняем проект в файл
                await _projectService.SaveProjectAsync(Project, projectFilePath);
                
                // Обновляем информацию о проекте
                if (CurrentProjectInfo != null)
                {
                    CurrentProjectInfo.FilePath = projectFilePath;
                    CurrentProjectInfo.Name = ProjectName ?? CurrentProjectInfo.Name;
                    CurrentProjectInfo.BookCount = Project.Books?.Count ?? 0;
                    // КРИТИЧЕСКИ ВАЖНО: PageCount - это количество разворотов в одной книге, а не сумма по всем книгам
                CurrentProjectInfo.PageCount = Project.Books?.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                    CurrentProjectInfo.Status = DetermineProjectStatus(Project);
                    CurrentProjectInfo.LastModified = DateTime.Now;
                }
                
                // Сохраняем обновленную информацию о проекте в список
                if (CurrentProjectInfo != null)
                {
                    await _projectListService.SaveProjectInfoAsync(CurrentProjectInfo);
                }
                
                // После сохранения возвращаемся на главную страницу со списком проектов
                CurrentMode = AppMode.ProjectList;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка сохранения проекта: {ex.Message}";
                _loggingService.LogError("Ошибка сохранения проекта", ex);
                System.Windows.MessageBox.Show(
                    $"Ошибка сохранения проекта: {ex.Message}\n\nПроверьте, что файл не используется другой программой.",
                    "Ошибка",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
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
        
        private ProjectStatus DetermineProjectStatus(Project project)
        {
            if (project.Books.Count == 0)
            {
                return ProjectStatus.NotFilled;
            }

            var allBooksReady = project.Books.All(b => b.IsValid);
            
            if (allBooksReady)
            {
                if (!string.IsNullOrEmpty(project.OutputFolder) && Directory.Exists(project.OutputFolder))
                {
                    return ProjectStatus.SuccessfullyCompleted;
                }
                
                return ProjectStatus.Ready;
            }
            
            return ProjectStatus.NotFilled;
        }

        public ObservableCollection<Book> Books { get; }

        public Book? SelectedBook
        {
            get => _selectedBook;
            set => SetProperty(ref _selectedBook, value);
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
            set
            {
                SetProperty(ref _project, value);
                if (value != null)
                {
                    Books.Clear();
                    foreach (var book in value.Books)
                    {
                        Books.Add(book);
                    }
                }
            }
        }

        public ICommand LoadFoldersCommand { get; }
        public ICommand SelectCoverCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ExportWithFolderCommand { get; }
        public ICommand BackCommand { get; private set; }
        public ICommand SaveProjectCommand { get; }
        public ICommand DeleteBookCommand { get; }
        public ICommand ResetProjectCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }
        public ICommand MovePageUpCommand { get; }
        public ICommand MovePageDownCommand { get; }
        public ICommand AssignPageNumberCommand { get; }

        /// <summary>Promotes a spread to be the book's cover, keeping the file it points at.</summary>
        public ICommand AssignCoverCommand { get; }

        /// <summary>
        /// Restores the original folder order of spreads (alphabetical by file name).
        /// Distinct from <see cref="ResetProjectCommand"/>, which wipes the project.
        /// </summary>
        public ICommand ResetOrderCommand { get; }

        public ICommand OpenHelpCommand { get; }
        

        private async Task LoadFoldersAsync()
        {
            ErrorMessage = null;

            try
            {
                // Получаем список уже загруженных папок
                var existingFolders = Project?.Books
                    .Where(b => !string.IsNullOrEmpty(b.FolderPath))
                    .Select(b => b.FolderPath!)
                    .ToList() ?? new List<string>();

                // Показываем диалог выбора папок с множественным выбором
                // Валидация выполняется внутри диалога
                var dialog = new Presentation.Dialogs.MultiFolderBrowserDialog(_fileService, existingFolders);
                dialog.Owner = System.Windows.Application.Current.MainWindow;
                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var folders = dialog.SelectedFolders;
                if (folders.Count == 0)
                {
                    // Если все папки удалены, очищаем проект
                    if (Project != null && existingFolders.Count > 0)
                    {
                        Project.Books.Clear();
                        Books.Clear();
                        _projectService.SaveState(Project);
                    }
                    return;
                }
                
                // Находим папки, которые были удалены из списка
                var removedFolders = existingFolders.Except(folders).ToList();
                
                // Если есть уже загруженные папки, добавляем только новые
                var newFolders = folders.Except(existingFolders).ToList();
                
                // Если нет новых папок и нет удаленных, ничего не делаем
                if (newFolders.Count == 0 && removedFolders.Count == 0 && existingFolders.Count > 0)
                {
                    // Пользователь не добавил новых папок и не удалил существующие
                    return;
                }

                // Валидация уже выполнена в диалоге, но проверим еще раз для безопасности
                var validationResult = await _fileService.ValidateFoldersDetailedAsync(folders);
                if (!validationResult.IsValid)
                {
                    ErrorMessage = validationResult.ErrorMessage ?? "Ошибка валидации папок.";
                    System.Windows.MessageBox.Show(
                        $"Не удалось загрузить папки:\n\n{validationResult.ErrorMessage}\n\nПожалуйста, исправьте ошибки и попробуйте снова.",
                        "Ошибка валидации",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    return;
                }

                IsLoading = true;

                // Первое действие с данными создаёт запись проекта — до этого момента
                // вкладка могла быть открыта без какого-либо проекта вообще.
                await EnsureProjectInfoAsync();

                // Если проект уже существует
                if (Project != null && existingFolders.Count > 0)
                {
                    // Удаляем книги для удаленных папок
                    if (removedFolders.Count > 0)
                    {
                        var booksToRemove = Project.Books
                            .Where(b => !string.IsNullOrEmpty(b.FolderPath) && 
                                       removedFolders.Contains(b.FolderPath))
                            .ToList();
                        
                        foreach (var book in booksToRemove)
                        {
                            Project.Books.Remove(book);
                            Books.Remove(book);
                        }
                        
                        _projectService.SaveState(Project);
                    }
                    
                    // Добавляем новые книги
                    if (newFolders.Count > 0)
                    {
                        var newProject = await _projectService.CreateProjectFromFoldersAsync(newFolders);
                        foreach (var book in newProject.Books)
                        {
                            Project.Books.Add(book);
                            Books.Add(book);
                        }
                        
                        _projectService.SaveState(Project);
                    }
                    
                    // Пересчитываем индексы книг
                    for (int i = 0; i < Project.Books.Count; i++)
                    {
                        Project.Books[i].BookIndex = i + 1;
                    }
                }
                else
                {
                    // Создаем новый проект из папок
                    Project = await _projectService.CreateProjectFromFoldersAsync(folders);
                    _projectService.SaveState(Project);
                    
                    // КРИТИЧЕСКИ ВАЖНО: НЕ создаем новый ProjectInfo при загрузке папок
                    // ProjectInfo должен быть создан только при создании проекта через StartScreenView
                    // Если CurrentProjectInfo == null, значит проект был создан неправильно
                    // В этом случае просто продолжаем работу без сохранения в список
                    // Пользователь должен будет сохранить проект вручную через кнопку "Сохранить проект"
                }
                
                // КРИТИЧЕСКИ ВАЖНО: НЕ загружаем все миниатюры сразу - это потребляет слишком много памяти
                // Миниатюры будут загружаться лениво при отображении через конвертеры
                // Устанавливаем ThumbnailPath только если миниатюра уже существует
                var booksToLoad = Project.Books.Where(b => 
                    newFolders.Contains(b.FolderPath ?? "")).ToList();
                
                foreach (var book in booksToLoad)
                {
                    if (book.Cover != null && !string.IsNullOrEmpty(book.Cover.SourcePath))
                    {
                        var thumbDir = Path.Combine(Path.GetTempPath(), "PhotoBookRenamer", "Thumbnails");
                        // КРИТИЧЕСКИ ВАЖНО: Используем хэш полного пути для создания уникального имени миниатюры
                        // Это предотвращает конфликты при одинаковых именах файлов в разных папках
                        var filePathHash = _imageService.GetFilePathHash(book.Cover.SourcePath);
                        var thumbName = $"{filePathHash}_thumb.jpg";
                        var thumbPath = Path.Combine(thumbDir, thumbName);
                        
                        // Устанавливаем путь к миниатюре только если она уже существует
                        if (File.Exists(thumbPath))
                        {
                            book.Cover.ThumbnailPath = thumbPath;
                        }
                    }
                    
                    foreach (var page in book.Pages.Where(p => !string.IsNullOrEmpty(p.SourcePath)))
                    {
                        var thumbDir = Path.Combine(Path.GetTempPath(), "PhotoBookRenamer", "Thumbnails");
                        // КРИТИЧЕСКИ ВАЖНО: Используем хэш полного пути для создания уникального имени миниатюры
                        // Это предотвращает конфликты при одинаковых именах файлов в разных папках
                        var filePathHash = _imageService.GetFilePathHash(page.SourcePath);
                        var thumbName = $"{filePathHash}_thumb.jpg";
                        var thumbPath = Path.Combine(thumbDir, thumbName);
                        
                        // Устанавливаем путь к миниатюре только если она уже существует
                        if (File.Exists(thumbPath))
                        {
                            page.ThumbnailPath = thumbPath;
                        }
                    }
                }
                
                // КРИТИЧЕСКИ ВАЖНО: Загружаем недостающие миниатюры в фоне для всех загруженных книг
                var allImagePaths = Project.Books
                    .SelectMany(b => b.Pages.Select(p => p.SourcePath).Concat(new[] { b.Cover?.SourcePath }))
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();
                
                // Загружаем миниатюры в фоне, чтобы не блокировать UI
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
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка: {ex.Message}";
                _loggingService.LogError("Ошибка загрузки папок", ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async void SelectCoverAsync(Book? book)
        {
            if (book == null) return;

            var files = await _fileService.GetJpegFilesAsync(book.FolderPath!);
            var selectedFiles = await _fileService.SelectFilesAsync();
            
            if (selectedFiles != null && selectedFiles.Length > 0)
            {
                book.Cover = new Page
                {
                    SourcePath = selectedFiles[0],
                    IsCover = true,
                    Index = 0
                };
            }
        }

        private async Task ExportAsync()
        {
            if (Project == null) return;

            // The options modal owns the copy so it can show a progress bar, and it runs
            // the export itself. This method only handles the surrounding bookkeeping.
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
                    // PageCount is the number of spreads in ONE book, not a sum.
                    CurrentProjectInfo.PageCount =
                        Project.Books?.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                    await _projectListService.SaveProjectInfoAsync(CurrentProjectInfo);
                }

                ErrorMessage = null;
                await ShowExportSuccessAsync(outputFolder);
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

        /// <summary>Success modal, with an optional "open the folder" action.</summary>
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

        /// <summary>
        /// Ctrl+Shift+S. Same modal as <see cref="ExportAsync"/> - the only difference
        /// used to be that the target folder was picked without a name prompt, which the
        /// new options modal handles itself.
        /// </summary>
        private async Task ExportWithFolderAsync()
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
                    CurrentProjectInfo.PageCount =
                        Project.Books?.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                    await _projectListService.SaveProjectInfoAsync(CurrentProjectInfo);
                }

                ErrorMessage = null;
                await ShowExportSuccessAsync(outputFolder);
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

        private void DeleteBook(Book? book)
        {
            if (book == null || Project == null) return;

            _projectService.SaveState(Project);
            
            Books.Remove(book);
            Project.Books.Remove(book);
            
            // Пересчитываем индексы книг
            for (int i = 0; i < Project.Books.Count; i++)
            {
                Project.Books[i].BookIndex = i + 1;
            }
        }

        private void ResetProject()
        {
            Project = null;
            Books.Clear();
            ErrorMessage = null;
            _projectService.ClearHistory();
        }

        private void Undo()
        {
            if (Project == null) return;
            var previousProject = _projectService.Undo(Project);
            if (previousProject != null)
            {
                Project = previousProject;
            }
        }

        private void Redo()
        {
            if (Project == null) return;
            var nextProject = _projectService.Redo(Project);
            if (nextProject != null)
            {
                Project = nextProject;
            }
        }

        private void MovePageUp(Page? page)
        {
            if (page == null || page.IsCover) return;
            
            var book = Books.FirstOrDefault(b => b.Pages.Contains(page));
            if (book == null) return;

            // Находим индекс страницы в коллекции (исключая обложку)
            var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
            var pageIndex = pagesWithoutCover.IndexOf(page);
            if (pageIndex <= 0) return; // Уже на первой позиции

            _projectService.SaveState(Project!);
            
            // Перемещаем страницу в коллекции (влево - на позицию раньше)
            var currentIndex = book.Pages.IndexOf(page);
            var targetIndex = currentIndex - 1;
            
            // Убеждаемся, что не перемещаем на позицию обложки
            if (targetIndex >= 0 && !book.Pages[targetIndex].IsCover)
            {
                book.Pages.Move(currentIndex, targetIndex);
                UpdatePageDisplayIndices(book);
                // PageSlots обновится автоматически через CollectionChanged
            }
        }

        private void MovePageDown(Page? page)
        {
            if (page == null || page.IsCover) return;
            
            var book = Books.FirstOrDefault(b => b.Pages.Contains(page));
            if (book == null) return;

            // Находим индекс страницы в коллекции (исключая обложку)
            var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
            var pageIndex = pagesWithoutCover.IndexOf(page);
            if (pageIndex >= pagesWithoutCover.Count - 1) return; // Уже на последней позиции

            _projectService.SaveState(Project!);
            
            // Перемещаем страницу в коллекции (вправо - на позицию позже)
            var currentIndex = book.Pages.IndexOf(page);
            var targetIndex = currentIndex + 1;
            
            // Убеждаемся, что не выходим за границы
            if (targetIndex < book.Pages.Count)
            {
                book.Pages.Move(currentIndex, targetIndex);
                UpdatePageDisplayIndices(book);
                // PageSlots обновится автоматически через CollectionChanged
            }
        }

        private void AssignPageNumber(Page? page)
        {
            if (page == null || page.IsCover) return;
            
            var book = Books.FirstOrDefault(b => b.Pages.Contains(page));
            if (book == null) return;

            var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
            var maxPageNumber = pagesWithoutCover.Count;
            
            if (maxPageNumber == 0) return;

            var dialog = new Presentation.Dialogs.PageNumberDialog(maxPageNumber);
            dialog.Owner = System.Windows.Application.Current.MainWindow;
            if (dialog.ShowDialog() == true && dialog.SelectedPageNumber.HasValue)
            {
                var targetPageNumber = dialog.SelectedPageNumber.Value;
                
                _projectService.SaveState(Project!);
                
                // Находим текущую позицию страницы в коллекции
                var currentIndex = book.Pages.IndexOf(page);
                
                // Находим целевую позицию в коллекции (targetPageNumber, так как обложка на позиции 0)
                // Страницы начинаются с 1, но в коллекции они идут после обложки (индекс 1, 2, 3...)
                var targetIndexInCollection = targetPageNumber; // Позиция после обложки
                
                if (currentIndex == targetIndexInCollection)
                {
                    // Страница уже на нужной позиции
                    return;
                }
                
                // Удаляем страницу из текущей позиции
                book.Pages.RemoveAt(currentIndex);
                
                // Если удалили элемент до целевой позиции, нужно скорректировать индекс
                if (currentIndex < targetIndexInCollection)
                {
                    targetIndexInCollection--;
                }
                
                // Вставляем на новую позицию
                if (targetIndexInCollection <= book.Pages.Count)
                {
                    book.Pages.Insert(targetIndexInCollection, page);
                }
                else
                {
                    book.Pages.Add(page);
                }
                
                UpdatePageDisplayIndices(book);
                // PageSlots обновится автоматически через CollectionChanged
            }
        }

        private void UpdatePageDisplayIndices(Book book)
        {
            // Обновляем DisplayIndex на основе позиции в коллекции (исключая обложку)
            var displayIndex = 1;
            foreach (var p in book.Pages.Where(p => !p.IsCover))
            {
                p.DisplayIndex = displayIndex;
                p.Index = displayIndex;
                displayIndex++;
            }
        }

        /// <summary>
        /// Makes <paramref name="page"/> the cover of its book, keeping the file it already
        /// points at. The spread it used to be does not become a duplicate: the slot is
        /// simply given up, and the remaining spreads are renumbered.
        /// </summary>
        private void AssignCover(Page? page)
        {
            if (page == null || page.IsCover) return;
            if (Project == null) return;

            var book = Books.FirstOrDefault(b => b.Pages.Contains(page));
            if (book == null) return;

            var oldCover = book.Cover;

            _projectService.SaveState(Project);

            // The promoted page leaves the spread list and becomes the cover.
            book.Pages.Remove(page);
            page.IsCover = true;
            page.Index = 0;
            page.DisplayIndex = 0;

            book.Cover = new Page
            {
                SourcePath = page.SourcePath,
                ThumbnailPath = page.ThumbnailPath,
                FileName = page.FileName,
                IsLocked = page.IsLocked,
                IsCover = true,
                Index = 0,
                DisplayIndex = 0
            };

            // The previous cover becomes a normal first spread instead of being lost.
            if (oldCover != null && !string.IsNullOrEmpty(oldCover.SourcePath))
            {
                oldCover.IsCover = false;
                book.Pages.Insert(0, oldCover);
            }

            UpdatePageDisplayIndices(book);
            book.UpdatePageSlots();
            OnPropertyChanged(nameof(Books));
            OnPropertyChanged(nameof(BooksSummary));
        }

        /// <summary>
        /// Restores spreads to the order they had when the folders were first read
        /// (alphabetical by file name). Does not touch covers or project metadata.
        /// </summary>
        private void ResetOrder()
        {
            if (Project == null || Books.Count == 0) return;

            bool changed = false;
            foreach (var book in Books)
            {
                var current = book.Pages.Where(p => !p.IsCover).ToList();
                var sorted = current
                    .OrderBy(p => p.SourcePath, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (current.SequenceEqual(sorted)) continue;

                _projectService.SaveState(Project);

                foreach (var p in current) book.Pages.Remove(p);
                foreach (var p in sorted) book.Pages.Add(p);

                UpdatePageDisplayIndices(book);
                book.UpdatePageSlots();
                changed = true;
            }

            if (changed)
            {
                OnPropertyChanged(nameof(Books));
                OnPropertyChanged(nameof(BooksSummary));
            }
        }

        private async Task LoadThumbnailForPage(Page page)
        {
            if (string.IsNullOrEmpty(page.SourcePath)) return;

            var thumbDir = Path.Combine(Path.GetTempPath(), "PhotoBookRenamer", "Thumbnails");
            if (!Directory.Exists(thumbDir))
            {
                Directory.CreateDirectory(thumbDir);
            }

            // КРИТИЧЕСКИ ВАЖНО: Используем хэш полного пути для создания уникального имени миниатюры
            // Это предотвращает конфликты при одинаковых именах файлов в разных папках
            var filePathHash = _imageService.GetFilePathHash(page.SourcePath);
            var thumbName = $"{filePathHash}_thumb.jpg";
            var thumbPath = Path.Combine(thumbDir, thumbName);

            if (!File.Exists(thumbPath))
            {
                await _imageService.CreateThumbnailAsync(page.SourcePath, thumbPath);
            }

            page.ThumbnailPath = thumbPath;
        }

        private async void OpenHelp()
        {
            // Автоматически сохраняем проект перед переходом в помощь
            if (CurrentProjectInfo != null && Project != null && Project.Books != null && Project.Books.Count > 0)
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
                    .OpenHelp(HelpSection.UniqueFolders);
            });
        }
        
        private async Task SaveProjectSilentlyAsync()
        {
            if (CurrentProjectInfo == null || Project == null)
            {
                return;
            }
            
            try
            {
                var projectId = CurrentProjectInfo?.Id ?? string.Empty;
                if (string.IsNullOrEmpty(projectId))
                {
                    projectId = Guid.NewGuid().ToString();
                    if (CurrentProjectInfo != null)
                    {
                        CurrentProjectInfo.Id = projectId;
                    }
                }
                
                var projectsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhotoBookRenamer",
                    "Projects");
                
                if (!Directory.Exists(projectsDir))
                {
                    Directory.CreateDirectory(projectsDir);
                }
                
                var projectFilePath = Path.Combine(projectsDir, $"{projectId}.json");
                
                // Сохраняем проект в файл
                await _projectService.SaveProjectAsync(Project, projectFilePath);
                
                // Обновляем информацию о проекте
                if (CurrentProjectInfo != null)
                {
                    CurrentProjectInfo.FilePath = projectFilePath;
                    CurrentProjectInfo.Name = ProjectName ?? CurrentProjectInfo.Name;
                    CurrentProjectInfo.BookCount = Project.Books?.Count ?? 0;
                    CurrentProjectInfo.PageCount = Project.Books?.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                    CurrentProjectInfo.Status = DetermineProjectStatus(Project);
                    CurrentProjectInfo.LastModified = DateTime.Now;
                }
                
                // Сохраняем обновленную информацию о проекте в список
                if (CurrentProjectInfo != null)
                {
                    await _projectListService.SaveProjectInfoAsync(CurrentProjectInfo);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Ошибка автоматического сохранения проекта", ex);
                // Не показываем ошибку пользователю при автоматическом сохранении
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

            var info = await _projectListService.CreateProjectAsync(AppMode.UniqueFolders, name);
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
            if (Project == null || Project.Books == null || Project.Books.Count == 0)
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
    }
}

