using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoBookRenamer.Domain;
using PhotoBookRenamer.Application;
using PhotoBookRenamer.Presentation.Views;

namespace PhotoBookRenamer.Presentation.ViewModels
{
    public class ProjectListViewModel : ViewModelBase
    {
        private readonly IProjectListService _projectListService;
        private bool _isLoading;
        private string? _errorMessage;
        private ProjectInfo? _selectedProject;

        public ProjectListViewModel(IProjectListService projectListService)
        {
            _projectListService = projectListService;
            Projects = new ObservableCollection<ProjectInfo>();
            
            LoadProjectsCommand = new AsyncRelayCommand(LoadProjectsAsync);
            CreateProjectCommand = new RelayCommand(CreateProject);
            OpenProjectCommand = new RelayCommand<ProjectInfo>(OpenProject);
            DeleteProjectCommand = new AsyncRelayCommand<ProjectInfo>(DeleteProjectAsync, CanDeleteProject);
            
            // Загружаем проекты при создании
            // CollectionChanged -> ProjectsCount, so "N проектов" and the empty state
            // re-evaluate. See the remark on ProjectsCount.
            Projects.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ProjectsCount));
            _ = LoadProjectsAsync();
        }

        public ObservableCollection<ProjectInfo> Projects { get; }

        /// <summary>
        /// Count for bindings that must react to items being added.
        ///
        /// WPF does not re-evaluate a binding just because an ObservableCollection grew:
        /// it only listens for INotifyPropertyChanged on the binding source. Binding
        /// straight to <c>Projects</c> therefore latched onto whatever the collection
        /// contained at load time (empty), and the "no projects yet" panel stayed on top
        /// of the freshly loaded cards. Subscribing to CollectionChanged and re-raising is
        /// what makes such bindings update.
        /// </summary>
        public int ProjectsCount => Projects.Count;

        public ProjectInfo? SelectedProject
        {
            get => _selectedProject;
            set
            {
                SetProperty(ref _selectedProject, value);
                ((AsyncRelayCommand<ProjectInfo>)DeleteProjectCommand).NotifyCanExecuteChanged();
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

        public string ModeTitle => "Все проекты";

        public ICommand LoadProjectsCommand { get; }
        public ICommand CreateProjectCommand { get; }
        public ICommand OpenProjectCommand { get; }
        public ICommand DeleteProjectCommand { get; }
        public ICommand BackCommand { get; }

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

        /// <summary>
        /// Opening a saved project is one of the two ways into an editor. The mode tab is
        /// disabled otherwise, so this has to go through OpenProject rather than a plain
        /// CurrentMode change - the setter would refuse it.
        /// </summary>
        private void OpenProjectMode(AppMode mode)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var serviceProvider = ((App)System.Windows.Application.Current).GetServiceProvider();
                var mainVm = serviceProvider?.GetRequiredService<MainViewModel>();
                mainVm?.OpenProject(mode);
            });
        }

        private async Task LoadProjectsAsync()
        {
            // Two loads can start at once: the constructor fires one and BuildView() asks
            // for another right after creating the ViewModel. Each of them only clears
            // Projects after awaiting the file reads, so they interleave as
            // clear / clear / add / add and every project shows up twice. A second load
            // while one is running reads the same data anyway, so skipping it is safe.
            if (_isLoading) return;

            IsLoading = true;
            ErrorMessage = null;

            try
            {
                var serviceProvider = ((App)System.Windows.Application.Current).GetServiceProvider();
                if (serviceProvider == null)
                {
                    IsLoading = false;
                    return;
                }

                var projectService = serviceProvider.GetRequiredService<IProjectService>();
                var projects = await _projectListService.GetAllProjectsAsync();
                Projects.Clear();
                
                // КРИТИЧЕСКИ ВАЖНО: Создаём копии проектов, чтобы каждый проект в списке был независимым
                // Это гарантирует, что при открытии проекта используется правильный projectInfo
                // Сортируем по дате изменения (новые сверху)
                foreach (var project in projects.OrderByDescending(p => p.LastModified))
                {
                    // КРИТИЧЕСКИ ВАЖНО: Проверяем, что у проекта есть ID
                    if (string.IsNullOrEmpty(project.Id))
                    {
                        // Пропускаем проекты без ID
                        continue;
                    }
                    
                    // КРИТИЧЕСКИ ВАЖНО: Пересчитываем PageCount на основе реальных данных проекта
                    // Загружаем проект из файла и пересчитываем PageCount
                    int actualPageCount = project.PageCount;
                    int actualBookCount = project.BookCount;
                    Project? previewSource = null;
                    
                    if (!string.IsNullOrEmpty(project.FilePath) && File.Exists(project.FilePath))
                    {
                        try
                        {
                            var loadedProject = await projectService.LoadProjectAsync(project.FilePath);
                            if (loadedProject != null && loadedProject.Books != null && loadedProject.Books.Count > 0)
                            {
                                // КРИТИЧЕСКИ ВАЖНО: PageCount - это количество разворотов в одной книге, а не сумма по всем книгам
                                // Во всех книгах должно быть одинаковое количество разворотов
                                actualPageCount = loadedProject.Books.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                                actualBookCount = loadedProject.Books.Count;

                                // The mockup's project card leads with a strip of three
                                // 48x48 photos plus a "+N" tile. The project is already
                                // fully loaded here, so the paths are simply read off it
                                // rather than re-opened per card.
                                previewSource = loadedProject;
                                
                                // Обновляем PageCount в сохранённом ProjectInfo, если он изменился
                                if (actualPageCount != project.PageCount || actualBookCount != project.BookCount)
                                {
                                    project.PageCount = actualPageCount;
                                    project.BookCount = actualBookCount;
                                    // Сохраняем обновлённую информацию в фоне
                                    Background.Run(
                                        async () => await _projectListService.SaveProjectInfoAsync(project),
                                        "save project index");
                                }
                            }
                        }
                        catch
                        {
                            // Игнорируем ошибки загрузки отдельных проектов
                        }
                    }
                    
                    // Создаём копию проекта с правильными данными
                    var projectCopy = new ProjectInfo
                    {
                        Id = project.Id, // ВСЕГДА используем оригинальный ID
                        Name = project.Name,
                        FilePath = project.FilePath,
                        Mode = project.Mode,
                        BookCount = actualBookCount,
                        PageCount = actualPageCount,
                        Status = project.Status,
                        CreatedDate = project.CreatedDate,
                        LastModified = project.LastModified
                    };

                    // Preview strip: 3 photos + "+N". Filled before the card is added to
                    // Projects so the template's first layout pass already has the data.
                    if (previewSource != null)
                    {
                        FillPreviewPhotos(previewSource, projectCopy);
                    }

                    Projects.Add(projectCopy);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка загрузки проектов: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// How many photos the card shows as thumbnails. The mockup uses three, then
        /// puts the remainder in a "+N" tile.
        /// </summary>
        private const int PreviewPhotoLimit = 3;

        /// <summary>
        /// Fills a project card's preview strip from an already-loaded project.
        ///
        /// Covers come first, then spreads, book by book, because that is the order the
        /// photos appear in the finished book. Only paths that still exist on disk are
        /// taken: a project can outlive a moved or deleted photo, and a broken image in
        /// the strip would be worse than one tile fewer.
        /// </summary>
        private static void FillPreviewPhotos(Project project, ProjectInfo target)
        {
            if (project?.Books == null) return;

            int total = 0;

            foreach (var book in project.Books)
            {
                if (book == null) continue;

                // The cover is a separate object from Pages, and it is the first thing
                // the reader sees, so it leads the strip.
                if (book.Cover != null && !string.IsNullOrEmpty(book.Cover.SourcePath))
                {
                    total++;
                    if (target.PreviewPhotos.Count < PreviewPhotoLimit && File.Exists(book.Cover.SourcePath))
                        target.PreviewPhotos.Add(book.Cover.SourcePath);
                }

                if (book.Pages == null) continue;

                foreach (var page in book.Pages)
                {
                    if (page == null || string.IsNullOrEmpty(page.SourcePath)) continue;

                    total++;
                    if (target.PreviewPhotos.Count < PreviewPhotoLimit && File.Exists(page.SourcePath))
                        target.PreviewPhotos.Add(page.SourcePath);
                }
            }

            target.TotalPhotoCount = total;
        }

        private void CreateProject()
        {
            // Переходим на страницу выбора режима
            CurrentMode = AppMode.StartScreen;
        }

        private void OpenProject(ProjectInfo? projectInfo)
        {
            if (projectInfo != null)
            {
                // КРИТИЧЕСКИ ВАЖНО: Создаём копию projectInfo, чтобы гарантировать правильность данных
                // Это предотвращает использование изменённой ссылки
                var projectInfoCopy = new ProjectInfo
                {
                    Id = projectInfo.Id, // ВСЕГДА используем оригинальный ID
                    Name = projectInfo.Name,
                    FilePath = projectInfo.FilePath,
                    Mode = projectInfo.Mode,
                    BookCount = projectInfo.BookCount,
                    PageCount = projectInfo.PageCount,
                    Status = projectInfo.Status,
                    CreatedDate = projectInfo.CreatedDate,
                    LastModified = projectInfo.LastModified
                };
                _ = OpenProjectAsync(projectInfoCopy);
            }
        }

        private async Task OpenProjectAsync(ProjectInfo projectInfo)
        {
            try
            {
                var serviceProvider = ((App)System.Windows.Application.Current).GetServiceProvider();
                if (serviceProvider == null)
                {
                    return;
                }

                var projectService = serviceProvider.GetRequiredService<IProjectService>();
                
                // КРИТИЧЕСКИ ВАЖНО: Сохраняем ВСЕ данные проекта в локальные переменные СРАЗУ
                // Это гарантирует, что мы используем правильные данные выбранного проекта
                // НЕ используем projectInfo напрямую, так как он может быть изменён
                var projectId = projectInfo.Id ?? string.Empty;
                var projectName = projectInfo.Name ?? string.Empty;
                var projectMode = projectInfo.Mode;
                var projectBookCount = projectInfo.BookCount;
                var projectPageCount = projectInfo.PageCount;
                var projectStatus = projectInfo.Status;
                var projectLastModified = projectInfo.LastModified;
                var projectCreatedDate = projectInfo.CreatedDate;
                
                // КРИТИЧЕСКИ ВАЖНО: Проверяем, что Id не пустой
                if (string.IsNullOrEmpty(projectId))
                {
                    ErrorMessage = $"Ошибка: проект '{projectName}' не имеет Id. Невозможно открыть проект.";
                    return;
                }
                
                // КРИТИЧЕСКИ ВАЖНО: Формируем FilePath на основе Id проекта
                // В новой системе файлы хранятся прямо в папке Projects, формат: {ProjectId}.json
                var projectsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhotoBookRenamer",
                    "Projects");
                
                // Создаём папку, если её нет
                if (!Directory.Exists(projectsDir))
                {
                    Directory.CreateDirectory(projectsDir);
                }
                
                // ВСЕГДА формируем путь на основе Id - это единственный надежный способ
                var projectFilePath = Path.Combine(projectsDir, $"{projectId}.json");
                
                // КРИТИЧЕСКИ ВАЖНО: Загружаем проект из файла
                Project? project = null;
                
                // КРИТИЧЕСКИ ВАЖНО: Проверяем существование файла и загружаем проект
                // ВСЕГДА используем путь, сформированный на основе ID проекта
                if (System.IO.File.Exists(projectFilePath))
                {
                    try
                    {
                        project = await projectService.LoadProjectAsync(projectFilePath);
                        
                        // Если проект не загрузился, создаем новый
                        if (project == null)
                        {
                            project = new Project { Mode = projectMode };
                        }
                        // Если Books null (не должно быть), создаем новый проект
                        else if (project.Books == null)
                        {
                            project = new Project { Mode = projectMode };
                        }
                    }
                    catch (Exception ex)
                    {
                        // Если ошибка загрузки, показываем ошибку и создаем пустой проект
                        ErrorMessage = $"Ошибка загрузки проекта из файла {projectFilePath}: {ex.Message}";
                        project = new Project { Mode = projectMode };
                    }
                }
                else
                {
                    // Если файла нет, создаем пустой проект
                    // Это может быть новый проект, который еще не был сохранен
                    project = new Project { Mode = projectMode };
                }

                // КРИТИЧЕСКИ ВАЖНО: Создаём НОВЫЙ projectInfo с сохранёнными данными
                // Это гарантирует, что мы используем правильный projectInfo для этого проекта
                // ВСЕГДА используем данные из локальных переменных, а не из projectInfo
                var projectInfoCopy = new ProjectInfo
                {
                    Id = projectId, // ВСЕГДА используем ID из выбранного проекта (из локальной переменной)
                    Name = projectName,
                    FilePath = projectFilePath, // ВСЕГДА формируем на основе ID
                    Mode = projectMode,
                    BookCount = projectBookCount,
                    PageCount = projectPageCount,
                    Status = projectStatus,
                    CreatedDate = projectCreatedDate,
                    LastModified = projectLastModified
                };
                
                // КРИТИЧЕСКИ ВАЖНО: Сохраняем данные проекта в замыкание ДО переключения режима
                // Это гарантирует, что мы используем правильные данные выбранного проекта
                var capturedProjectId = projectId;
                var capturedProjectName = projectName;
                var capturedProjectFilePath = projectFilePath;
                var capturedProjectMode = projectMode;
                var capturedProjectBookCount = projectBookCount;
                var capturedProjectPageCount = projectPageCount;
                var capturedProjectStatus = projectStatus;
                var capturedProjectCreatedDate = projectCreatedDate;
                var capturedProjectLastModified = projectLastModified;
                var capturedProject = project;
                
                // ОТЛАДКА: Логируем захваченные данные
                
                // Переключаемся на режим редактирования проекта ПОСЛЕ сохранения данных
                OpenProjectMode(projectMode);
                
                // КРИТИЧЕСКИ ВАЖНО: Создаём НОВЫЙ projectInfo из захваченных данных ПЕРЕД асинхронным вызовом
                // Это гарантирует, что мы используем правильные данные выбранного проекта
                var finalProjectInfo = new ProjectInfo
                {
                    Id = capturedProjectId, // ВСЕГДА используем ID из захваченных данных
                    Name = capturedProjectName,
                    FilePath = capturedProjectFilePath,
                    Mode = capturedProjectMode,
                    BookCount = capturedProjectBookCount,
                    Status = capturedProjectStatus,
                    CreatedDate = capturedProjectCreatedDate,
                    LastModified = capturedProjectLastModified
                };
                
                // Устанавливаем проект в ViewModel после того, как MainViewModel создаст View
                // Используем двойной BeginInvoke для гарантии, что View создан
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Loaded,
                    new System.Action(() =>
                    {
                        // Еще один BeginInvoke для гарантии, что View полностью инициализирован
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(
                            System.Windows.Threading.DispatcherPriority.Loaded,
                            new System.Action(() =>
                            {
                                if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
                                {
                                    var mainVm = serviceProvider.GetRequiredService<MainViewModel>();
                                    
                                    // КРИТИЧЕСКИ ВАЖНО: Получаем ViewModel напрямую из сервиса, а не из View
                                    // Это гарантирует, что мы используем правильный ViewModel
                                    if (capturedProjectMode == AppMode.UniqueFolders)
                                    {
                                        var uniqueVm = serviceProvider.GetRequiredService<UniqueFoldersViewModel>();
                                        
                                        // КРИТИЧЕСКИ ВАЖНО: Создаём ЕЩЁ ОДИН НОВЫЙ projectInfo из захваченных данных
                                        // НЕ используем finalProjectInfo, так как он может быть изменён
                                        // ВСЕГДА используем захваченные данные напрямую
                                        var setProjectInfo = new ProjectInfo
                                        {
                                            Id = capturedProjectId, // ВСЕГДА используем ID из захваченных данных
                                            Name = capturedProjectName,
                                            FilePath = capturedProjectFilePath,
                                            Mode = capturedProjectMode,
                                            BookCount = capturedProjectBookCount,
                                            PageCount = capturedProjectPageCount,
                                            Status = capturedProjectStatus,
                                            CreatedDate = capturedProjectCreatedDate,
                                            LastModified = capturedProjectLastModified
                                        };
                                        
                                        // КРИТИЧЕСКИ ВАЖНО: Используем захваченные данные и загруженный project
                                        uniqueVm.SetProject(capturedProject, setProjectInfo);
                                    }
                                    else if (capturedProjectMode == AppMode.Combined)
                                    {
                                        var combinedVm = serviceProvider.GetRequiredService<CombinedModeViewModel>();
                                        
                                        // КРИТИЧЕСКИ ВАЖНО: Создаём ЕЩЁ ОДИН НОВЫЙ projectInfo из захваченных данных
                                        var setProjectInfo = new ProjectInfo
                                        {
                                            Id = capturedProjectId,
                                            Name = capturedProjectName,
                                            FilePath = capturedProjectFilePath,
                                            Mode = capturedProjectMode,
                                            BookCount = capturedProjectBookCount,
                                            PageCount = capturedProjectPageCount,
                                            Status = capturedProjectStatus,
                                            CreatedDate = capturedProjectCreatedDate,
                                            LastModified = capturedProjectLastModified
                                        };
                                        
                                        combinedVm.SetProject(capturedProject, setProjectInfo);
                                    }
                                }
                            }));
                    }));
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка открытия проекта: {ex.Message}";
            }
        }

        private bool CanDeleteProject(ProjectInfo? projectInfo)
        {
            return projectInfo != null;
        }

        private async Task DeleteProjectAsync(ProjectInfo? projectInfo)
        {
            if (projectInfo == null) return;

            var result = Presentation.Dialogs.AppDialogs.Confirm(
                "Удалить проект?",
                $"Проект \"{projectInfo.Name}\" будет удалён вместе со своей структурой.\n\n" +
                "Фотографии на диске останутся на месте — удаляется только проект.",
                "Удалить",
                destructive: true);

            if (result)
            {
                try
                {
                    var success = await _projectListService.DeleteProjectAsync(projectInfo);
                    if (success)
                    {
                        Projects.Remove(projectInfo);
                    }
                    else
                    {
                        ErrorMessage = "Не удалось удалить проект";
                    }
                }
                catch (Exception ex)
                {
                    ErrorMessage = $"Ошибка удаления проекта: {ex.Message}";
                }
            }
        }
    }
}


