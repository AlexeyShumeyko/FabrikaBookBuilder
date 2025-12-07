using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PhotoBookRenamer.Models;

namespace PhotoBookRenamer.Services
{
    public class ProjectListService : IProjectListService
    {
        private readonly string _projectsDirectory;
        private readonly string _projectsListPath;

        public ProjectListService()
        {
            // КРИТИЧЕСКИ ВАЖНО: Используем LocalApplicationData, а не путь к программе
            // Это гарантирует, что проекты сохраняются в правильном месте независимо от расположения программы
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhotoBookRenamer",
                "Projects");
            _projectsDirectory = appDataPath;
            _projectsListPath = Path.Combine(_projectsDirectory, "projects.json");
            
            // Создаём папку, если её нет
            if (!Directory.Exists(_projectsDirectory))
            {
                try
                {
                    Directory.CreateDirectory(_projectsDirectory);
                }
                catch (Exception ex)
                {
                }
            }
            
        }

        /// <summary>
        /// Получает путь к файлу проекта по его ID
        /// </summary>
        private string GetProjectFilePath(string projectId)
        {
            return Path.Combine(_projectsDirectory, $"{projectId}.json");
        }

        /// <summary>
        /// Загружает список всех проектов из projects.json
        /// </summary>
        public async Task<List<ProjectInfo>> GetProjectsAsync(AppMode mode)
        {
            try
            {
                var allProjects = await GetAllProjectsAsync();
                return allProjects.Where(p => p.Mode == mode).ToList();
            }
            catch
            {
                return new List<ProjectInfo>();
            }
        }

        /// <summary>
        /// Загружает список всех проектов из projects.json
        /// </summary>
        public async Task<List<ProjectInfo>> GetAllProjectsAsync()
        {
            try
            {
                if (!File.Exists(_projectsListPath))
                {
                    // Попытка миграции старых проектов
                    await MigrateOldProjectsAsync();
                    if (!File.Exists(_projectsListPath))
                    {
                        return new List<ProjectInfo>();
                    }
                }

                var json = await File.ReadAllTextAsync(_projectsListPath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new List<ProjectInfo>();
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                var projects = JsonSerializer.Deserialize<List<ProjectInfo>>(json, options);
                
                if (projects == null)
                {
                    return new List<ProjectInfo>();
                }

                // КРИТИЧЕСКИ ВАЖНО: Валидация и исправление проектов
                var validatedProjects = new List<ProjectInfo>();
                var usedIds = new HashSet<string>();

                foreach (var project in projects)
                {
                    // Если Id пустой или дублируется, генерируем новый уникальный
                    if (string.IsNullOrEmpty(project.Id) || usedIds.Contains(project.Id))
                    {
                        string newId;
                        do
                        {
                            newId = Guid.NewGuid().ToString();
                        } while (usedIds.Contains(newId) || validatedProjects.Any(p => p.Id == newId));
                        project.Id = newId;
                    }
                    usedIds.Add(project.Id);
                    
                    // ВСЕГДА формируем FilePath на основе Id
                    project.FilePath = GetProjectFilePath(project.Id);
                    
                    validatedProjects.Add(project);
                }

                // Сохраняем исправленный список, если были изменения
                if (validatedProjects.Count != projects.Count || 
                    validatedProjects.Any(p => p.Id != projects.FirstOrDefault(pr => pr.Name == p.Name)?.Id))
                {
                    await SaveProjectsListAsync(validatedProjects);
                }

                return validatedProjects;
            }
            catch
            {
                return new List<ProjectInfo>();
            }
        }

        /// <summary>
        /// Создает новый проект с уникальным GUID
        /// </summary>
        public async Task<ProjectInfo?> CreateProjectAsync(AppMode mode, string name)
        {
            try
            {
                var allProjects = await GetAllProjectsAsync();
                
                // Генерируем уникальный ID
                string projectId;
                do
                {
                    projectId = Guid.NewGuid().ToString();
                } while (allProjects.Any(p => p.Id == projectId));

                var now = DateTime.Now;
                var projectInfo = new ProjectInfo
                {
                    Id = projectId,
                    Name = string.IsNullOrWhiteSpace(name) ? $"Проект {now:yyyy-MM-dd HH:mm}" : name,
                    Mode = mode,
                    Status = ProjectStatus.NotFilled,
                    BookCount = 0,
                    PageCount = 0,
                    CreatedDate = now,
                    LastModified = now,
                    FilePath = GetProjectFilePath(projectId)
                };

                allProjects.Add(projectInfo);
                await SaveProjectsListAsync(allProjects);

                return projectInfo;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Сохраняет информацию о проекте в список проектов
        /// КРИТИЧЕСКИ ВАЖНО: Обновляет только проект с соответствующим ID
        /// </summary>
        public async Task<bool> SaveProjectInfoAsync(ProjectInfo projectInfo)
        {
            try
            {
                // КРИТИЧЕСКИ ВАЖНО: Проверяем, что ID установлен
                if (string.IsNullOrEmpty(projectInfo.Id))
                {
                    throw new InvalidOperationException("Нельзя сохранить проект без Id");
                }

                var projectId = projectInfo.Id;
                var projectFilePath = GetProjectFilePath(projectId);

                var allProjects = await GetAllProjectsAsync();
                
                // КРИТИЧЕСКИ ВАЖНО: Ищем проект ТОЛЬКО по ID
                var existingIndex = allProjects.FindIndex(p => p.Id == projectId);
                
                if (existingIndex >= 0)
                {
                    // Обновляем существующий проект - создаём новый объект с правильными данными
                    allProjects[existingIndex] = new ProjectInfo
                    {
                        Id = projectId, // ВСЕГДА используем оригинальный ID
                        Name = projectInfo.Name,
                        FilePath = projectFilePath, // ВСЕГДА формируем на основе ID
                        Mode = projectInfo.Mode,
                        BookCount = projectInfo.BookCount,
                        Status = projectInfo.Status,
                        LastModified = DateTime.Now
                    };
                }
                else
                {
                    // Если проект не найден, добавляем новый
                    projectInfo.FilePath = projectFilePath;
                    projectInfo.LastModified = DateTime.Now;
                    allProjects.Add(projectInfo);
                }

                await SaveProjectsListAsync(allProjects);
                return true;
            }
            catch (Exception ex)
            {
                // Логируем ошибку для отладки
                return false;
            }
        }

        /// <summary>
        /// Удаляет проект из списка и удаляет его файл
        /// </summary>
        public async Task<bool> DeleteProjectAsync(ProjectInfo projectInfo)
        {
            try
            {
                if (string.IsNullOrEmpty(projectInfo.Id))
                {
                    return false;
                }

                var allProjects = await GetAllProjectsAsync();
                var projectToDelete = allProjects.FirstOrDefault(p => p.Id == projectInfo.Id);
                
                if (projectToDelete != null)
                {
                    allProjects.Remove(projectToDelete);
                    await SaveProjectsListAsync(allProjects);
                    
                    // Удаляем файл проекта
                    var projectFilePath = GetProjectFilePath(projectInfo.Id);
                    if (File.Exists(projectFilePath))
                    {
                        File.Delete(projectFilePath);
                    }
                    
                    return true;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Обновляет информацию о проекте на основе загруженного Project
        /// </summary>
        public async Task<ProjectInfo?> UpdateProjectInfoAsync(Project project, string filePath)
        {
            try
            {
                // Извлекаем ID из пути к файлу
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                if (string.IsNullOrEmpty(fileName))
                {
                    return null;
                }

                var allProjects = await GetAllProjectsAsync();
                var projectInfo = allProjects.FirstOrDefault(p => p.Id == fileName);
                
                if (projectInfo != null)
                {
                    projectInfo.BookCount = project.Books?.Count ?? 0;
                    // КРИТИЧЕСКИ ВАЖНО: PageCount - это количество разворотов в одной книге, а не сумма по всем книгам
                    // Во всех книгах должно быть одинаковое количество разворотов
                    projectInfo.PageCount = project.Books?.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                    projectInfo.Status = DetermineStatus(project);
                    projectInfo.LastModified = DateTime.Now;
                    await SaveProjectInfoAsync(projectInfo);
                    return projectInfo;
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Сохраняет список всех проектов в projects.json
        /// </summary>
        private async Task SaveProjectsListAsync(List<ProjectInfo> projects)
        {
            try
            {
                // КРИТИЧЕСКИ ВАЖНО: Убеждаемся, что у всех проектов есть правильный ID и FilePath
                foreach (var project in projects)
                {
                    if (string.IsNullOrEmpty(project.Id))
                    {
                        project.Id = Guid.NewGuid().ToString();
                    }
                    project.FilePath = GetProjectFilePath(project.Id);
                }

                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                var json = JsonSerializer.Serialize(projects, options);
                await File.WriteAllTextAsync(_projectsListPath, json);
            }
            catch (Exception ex)
            {
            }
        }

        /// <summary>
        /// Определяет статус проекта
        /// </summary>
        private ProjectStatus DetermineStatus(Project project)
        {
            if (project.Books == null || project.Books.Count == 0)
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

        /// <summary>
        /// Миграция старых проектов в новую систему
        /// </summary>
        private async Task MigrateOldProjectsAsync()
        {
            try
            {
                var migratedProjects = new List<ProjectInfo>();

                // Ищем старые файлы проектов по режимам
                foreach (AppMode mode in Enum.GetValues(typeof(AppMode)))
                {
                    if (mode == AppMode.StartScreen || mode == AppMode.ProjectList)
                        continue;

                    var oldListPath = Path.Combine(_projectsDirectory, $"{mode}_projects.json");
                    if (File.Exists(oldListPath))
                    {
                        try
                        {
                            var json = await File.ReadAllTextAsync(oldListPath);
                            if (!string.IsNullOrWhiteSpace(json))
                            {
                                var options = new JsonSerializerOptions
                                {
                                    PropertyNameCaseInsensitive = true,
                                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                                };
                                var oldProjects = JsonSerializer.Deserialize<List<ProjectInfo>>(json, options);
                                
                                if (oldProjects != null)
                                {
                                    foreach (var oldProject in oldProjects)
                                    {
                                        // Если у старого проекта нет ID, генерируем новый
                                        if (string.IsNullOrEmpty(oldProject.Id))
                                        {
                                            oldProject.Id = Guid.NewGuid().ToString();
                                        }
                                        
                                        // Формируем правильный FilePath
                                        oldProject.FilePath = GetProjectFilePath(oldProject.Id);
                                        
                                        migratedProjects.Add(oldProject);
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Игнорируем ошибки миграции
                        }
                    }
                }

                // Сохраняем мигрированные проекты
                if (migratedProjects.Count > 0)
                {
                    await SaveProjectsListAsync(migratedProjects);
                }
            }
            catch
            {
                // Игнорируем ошибки миграции
            }
        }
    }
}
