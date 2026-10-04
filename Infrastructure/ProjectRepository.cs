using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using PhotoBook.Core;
using PhotoBookRenamer.Application;

namespace PhotoBookRenamer.Infrastructure
{
    /// <summary>
    /// Projects on disk, as JSON.
    /// </summary>
    /// <remarks>
    /// The layout is camelCase, indented, and is what 1.1.1 wrote - the format is a promise
    /// to the projects already saved on people's machines, so the options below are not
    /// free to change. tests\PhotoBook.Application.Tests\ProjectCompatibilityTests.cs opens a
    /// copy of that file and fails if it stops opening.
    /// </remarks>
    public class ProjectRepository : IProjectRepository
    {
        private readonly IImageService _imageService;

        public ProjectRepository(IImageService imageService)
        {
            _imageService = imageService;
        }

        public async Task SaveAsync(Project project, string filePath)
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

        public async Task<Project?> LoadAsync(string filePath)
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

                var projectData = JsonSerializer.Deserialize<ProjectData>(json, options);

                Project? project = null;
                if (projectData != null)
                {
                    project = new Project
                    {
                        Mode = projectData.Mode,
                        OutputFolder = projectData.OutputFolder
                    };

                    if (projectData.Books != null && projectData.Books.Count > 0)
                    {
                        foreach (var bookData in projectData.Books)
                        {
                            var book = new Book
                            {
                                FolderPath = bookData.FolderPath,
                                Name = bookData.Name,
                                Cover = bookData.Cover,
                                BookIndex = bookData.BookIndex
                            };

                            if (bookData.Pages != null && bookData.Pages.Count > 0)
                            {
                                foreach (var page in bookData.Pages)
                                {
                                    book.Pages.Add(page);
                                }
                            }

                            // Слоты страниц пересчитываются после того, как страницы добавлены
                            book.UpdatePageSlots();

                            project.Books.Add(book);
                        }
                    }

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
                    if (project.Books != null && project.Books.Count > 0)
                    {
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

                    // Проекты, сохранённые до появления размеров, получают их при первом
                    // открытии - и дальше они лежат в файле проекта.
                    await ProjectImageSizes.FillMissingAsync(project, _imageService);
                }

                return project;
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка загрузки проекта: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// The stored shape of a project. Separate from the model it is read into, so that a
    /// property which is not part of the file simply is not listed here, and a property of
    /// the model which is derived rather than stored stays out of the file.
    /// </summary>
    internal class ProjectData
    {
        public AppMode Mode { get; set; }
        public string? OutputFolder { get; set; }
        public List<BookData>? Books { get; set; }
        public List<string>? AvailableFiles { get; set; }
    }

    internal class BookData
    {
        public string? FolderPath { get; set; }
        public string? Name { get; set; }
        public Page? Cover { get; set; }
        public int BookIndex { get; set; }
        public List<Page>? Pages { get; set; }
    }
}
