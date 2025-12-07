using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using PhotoBookRenamer.Models;

namespace PhotoBookRenamer.Services
{
    // КРИТИЧЕСКИ ВАЖНО: Вспомогательный класс для десериализации Book с read-only коллекцией Pages
    internal class BookData
    {
        public string? FolderPath { get; set; }
        public string? Name { get; set; }
        public Page? Cover { get; set; }
        public int BookIndex { get; set; }
        public List<Page>? Pages { get; set; }
    }

    // КРИТИЧЕСКИ ВАЖНО: Вспомогательный класс для десериализации Project с read-only коллекциями
    internal class ProjectData
    {
        public AppMode Mode { get; set; }
        public string? OutputFolder { get; set; }
        public List<BookData>? Books { get; set; }
        public List<string>? AvailableFiles { get; set; }
    }

    public class ProjectService : IProjectService
    {
        private readonly IFileService _fileService;
        private readonly IImageService _imageService;
        private readonly Stack<Project> _undoStack = new();
        private readonly Stack<Project> _redoStack = new();

        public ProjectService(IFileService fileService, IImageService imageService)
        {
            _fileService = fileService;
            _imageService = imageService;
        }

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public async Task<Project> CreateProjectFromFoldersAsync(List<string> folderPaths)
        {
            var project = new Project { Mode = AppMode.UniqueFolders };

            // КРИТИЧЕСКИ ВАЖНО: Параллельная обработка папок для ускорения загрузки
            // Это позволяет обрабатывать несколько папок одновременно, используя все доступные ядра процессора
            var books = await Task.WhenAll(folderPaths.Select(async folderPath =>
            {
                var folderName = System.IO.Path.GetFileName(folderPath);
                
                var files = await _fileService.GetJpegFilesAsync(folderPath);
                var coverPath = await _imageService.DetectCoverAsync(files.ToArray());

                var book = new Book
                {
                    FolderPath = folderPath,
                    Name = folderName,
                    Cover = new Page
                    {
                        SourcePath = coverPath,
                        IsCover = true,
                        Index = 0
                    }
                };

                // Обложка НЕ добавляется в Pages, она только в свойстве Cover
                // Добавляем только страницы (не обложку)
                var pageIndex = 1;
                foreach (var file in files.Where(f => f != coverPath).OrderBy(f => f))
                {
                    book.Pages.Add(new Page
                    {
                        SourcePath = file,
                        IsCover = false,
                        Index = pageIndex,
                        DisplayIndex = pageIndex
                    });
                    pageIndex++;
                }

                return book;
            }));

            // Добавляем все книги в проект
            foreach (var book in books)
            {
                project.Books.Add(book);
            }

            // Устанавливаем индексы книг
            for (int i = 0; i < project.Books.Count; i++)
            {
                project.Books[i].BookIndex = i + 1;
            }

            return project;
        }

        public async Task SaveProjectAsync(Project project, string filePath)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(project, options);
                
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка сохранения проекта: {ex.Message}", ex);
            }
        }

        public async Task<Project?> LoadProjectAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return null;
                }

                var json = await File.ReadAllTextAsync(filePath);
                
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.Never
                };

                // КРИТИЧЕСКИ ВАЖНО: System.Text.Json не может десериализовать read-only коллекции напрямую
                // Используем промежуточный класс для десериализации
                var projectData = JsonSerializer.Deserialize<ProjectData>(json, options);
                
                Project? project = null;
                if (projectData != null)
                {
                    project = new Project
                    {
                        Mode = projectData.Mode,
                        OutputFolder = projectData.OutputFolder
                    };
                    
                    // КРИТИЧЕСКИ ВАЖНО: Добавляем книги в коллекцию после создания проекта
                    if (projectData.Books != null && projectData.Books.Count > 0)
                    {
                        foreach (var bookData in projectData.Books)
                        {
                            // КРИТИЧЕСКИ ВАЖНО: Создаём Book из BookData, так как Pages - read-only коллекция
                            var book = new Book
                            {
                                FolderPath = bookData.FolderPath,
                                Name = bookData.Name,
                                Cover = bookData.Cover,
                                BookIndex = bookData.BookIndex
                            };
                            
                            // КРИТИЧЕСКИ ВАЖНО: Добавляем страницы в read-only коллекцию Pages
                            if (bookData.Pages != null && bookData.Pages.Count > 0)
                            {
                                foreach (var page in bookData.Pages)
                                {
                                    book.Pages.Add(page);
                                }
                            }
                            
                            // Обновляем слоты страниц после добавления всех страниц
                            book.UpdatePageSlots();
                            
                            project.Books.Add(book);
                        }
                    }
                    
                    // Добавляем доступные файлы
                    if (projectData.AvailableFiles != null)
                    {
                        foreach (var file in projectData.AvailableFiles)
                        {
                            project.AvailableFiles.Add(file);
                        }
                    }
                }
                
                // ВАЖНО: После десериализации нужно убедиться, что все коллекции правильно инициализированы
                if (project != null)
                {
                    // КРИТИЧЕСКИ ВАЖНО: Убеждаемся, что Books инициализирована
                    if (project.Books != null && project.Books.Count > 0)
                    {
                        // Проверяем каждую книгу
                        foreach (var book in project.Books)
                        {
                            // Pages должна быть инициализирована конструктором или десериализацией
                            if (book.Pages == null)
                            {
                                // Это критическая ошибка - пропускаем эту книгу
                                continue;
                            }
                            
                            // Убеждаемся, что слоты страниц обновлены после десериализации
                            book.UpdatePageSlots();
                        }
                    }
                }
                
                return project;
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка загрузки проекта: {ex.Message}", ex);
            }
        }

        public void SaveState(Project project)
        {
            if (project == null) return;
            
            var clone = project.Clone();
            _undoStack.Push(clone);
            _redoStack.Clear(); // Очищаем redo при новом действии
        }

        public Project? Undo(Project currentProject)
        {
            if (!CanUndo || currentProject == null) return null;

            // Сохраняем текущее состояние в redo
            _redoStack.Push(currentProject.Clone());

            // Восстанавливаем предыдущее состояние
            var previousState = _undoStack.Pop();
            return previousState;
        }

        public Project? Redo(Project currentProject)
        {
            if (!CanRedo || currentProject == null) return null;

            // Сохраняем текущее состояние в undo
            _undoStack.Push(currentProject.Clone());

            // Восстанавливаем следующее состояние
            var nextState = _redoStack.Pop();
            return nextState;
        }

        public void ClearHistory()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }
}







