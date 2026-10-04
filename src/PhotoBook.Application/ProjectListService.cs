using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Core;

namespace PhotoBookRenamer.Application
{
    public class ProjectListService : IProjectListService
    {
        private readonly string _projectsDirectory;
        private readonly string _projectsListPath;

        /// <summary>
        /// Every read-modify-write of projects.json goes through this gate.
        ///
        /// The service is a SINGLETON and the index is one shared file, while callers save
        /// from several places at once without awaiting: CombinedModeViewModel fires
        /// SaveProjectInfoAsync off a background task, ProjectListViewModel saves each
        /// project while the list loads, the editors save on rename. Two of those read the
        /// same snapshot and the later write silently dropped the earlier one's entry -
        /// that is where "my project disappeared from the list" came from. Reading was
        /// not safe either: GetAllProjectsAsync used to repair the file as a side effect
        /// of reading it, so a plain read could overwrite a concurrent write.
        /// </summary>
        private readonly SemaphoreSlim _indexGate = new(1, 1);

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
            await _indexGate.WaitAsync();
            try
            {
                var projects = await ReadAllProjects_NoLock();

                // КРИТИЧЕСКИ ВАЖНО: Валидация и исправление проектов
                var validatedProjects = new List<ProjectInfo>();
                var usedIds = new HashSet<string>();
                bool repaired = false;

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
                        repaired = true;
                    }
                    usedIds.Add(project.Id);

                    // ВСЕГДА формируем FilePath на основе Id
                    string expected = GetProjectFilePath(project.Id);
                    if (project.FilePath != expected)
                    {
                        project.FilePath = expected;
                        repaired = true;
                    }

                    validatedProjects.Add(project);
                }

                // Сохраняем исправленный список, только если правки действительно были.
                // Раньше здесь сравнение шло по Name, из-за чего перезапись происходила
                // почти при каждом чтении.
                if (repaired)
                {
                    await WriteAllProjects_NoLock(validatedProjects);
                }

                return validatedProjects;
            }
            catch
            {
                return new List<ProjectInfo>();
            }
            finally
            {
                _indexGate.Release();
            }
        }

        /// <summary>
        /// Reads and deserializes the index. The caller must hold <see cref="_indexGate"/>.
        /// </summary>
        private async Task<List<ProjectInfo>> ReadAllProjects_NoLock()
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
            return JsonSerializer.Deserialize<List<ProjectInfo>>(json, options) ?? new List<ProjectInfo>();
        }

        /// <summary>
        /// Создает новый проект с уникальным GUID
        /// </summary>
        public async Task<ProjectInfo?> CreateProjectAsync(AppMode mode, string name)
        {
            await _indexGate.WaitAsync();
            try
            {
                var allProjects = await ReadAllProjects_NoLock();

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
                await WriteAllProjects_NoLock(allProjects);

                return projectInfo;
            }
            catch
            {
                return null;
            }
            finally
            {
                _indexGate.Release();
            }
        }

        /// <summary>
        /// Сохраняет информацию о проекте в список проектов
        /// КРИТИЧЕСКИ ВАЖНО: Обновляет только проект с соответствующим ID
        /// </summary>
        public async Task<bool> SaveProjectInfoAsync(ProjectInfo projectInfo)
        {
            // КРИТИЧЕСКИ ВАЖНО: Проверяем, что ID установлен
            if (string.IsNullOrEmpty(projectInfo.Id))
            {
                return false;
            }

            await _indexGate.WaitAsync();
            try
            {
                var projectId = projectInfo.Id;
                var projectFilePath = GetProjectFilePath(projectId);

                var allProjects = await ReadAllProjects_NoLock();

                // КРИТИЧЕСКИ ВАЖНО: Ищем проект ТОЛЬКО по ID
                var existingIndex = allProjects.FindIndex(p => p.Id == projectId);

                if (existingIndex >= 0)
                {
                    var existing = allProjects[existingIndex];

                    // Обновляем существующий проект. PageCount и CreatedDate переносим из
                    // записи индекса: их нет в том объекте, который присылают редакторы
                    // (там только то, что изменилось), и раньше они молча обнулялись -
                    // в карточке проекта пропадало число страниц, а сортировка по дате
                    // создания съезжала.
                    allProjects[existingIndex] = new ProjectInfo
                    {
                        Id = projectId, // ВСЕГДА используем оригинальный ID
                        Name = projectInfo.Name,
                        FilePath = projectFilePath, // ВСЕГДА формируем на основе ID
                        Mode = projectInfo.Mode,
                        BookCount = projectInfo.BookCount,
                        PageCount = projectInfo.PageCount > 0 ? projectInfo.PageCount : existing.PageCount,
                        CreatedDate = projectInfo.CreatedDate != default ? projectInfo.CreatedDate : existing.CreatedDate,
                        Status = projectInfo.Status,
                        LastModified = DateTime.Now
                    };
                }
                else
                {
                    // Если проект не найден, добавляем новый
                    projectInfo.FilePath = projectFilePath;
                    projectInfo.LastModified = DateTime.Now;
                    if (projectInfo.CreatedDate == default) projectInfo.CreatedDate = DateTime.Now;
                    allProjects.Add(projectInfo);
                }

                await WriteAllProjects_NoLock(allProjects);
                return true;
            }
            catch (Exception ex)
            {
                // Логируем ошибку для отладки
                return false;
            }
            finally
            {
                _indexGate.Release();
            }
        }

        /// <summary>
        /// Удаляет проект из списка и удаляет его файл
        /// </summary>
        public async Task<bool> DeleteProjectAsync(ProjectInfo projectInfo)
        {
            if (string.IsNullOrEmpty(projectInfo?.Id))
            {
                return false;
            }

            await _indexGate.WaitAsync();
            try
            {
                var allProjects = await ReadAllProjects_NoLock();
                var projectToDelete = allProjects.FirstOrDefault(p => p.Id == projectInfo.Id);

                if (projectToDelete == null)
                {
                    return false;
                }

                allProjects.Remove(projectToDelete);
                await WriteAllProjects_NoLock(allProjects);

                // Удаляем файл проекта
                var projectFilePath = GetProjectFilePath(projectInfo.Id);
                if (File.Exists(projectFilePath))
                {
                    File.Delete(projectFilePath);
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                _indexGate.Release();
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
        /// Serializes and writes the whole index. The caller must hold
        /// <see cref="_indexGate"/>.
        ///
        /// The write goes to a temporary file that then REPLACES the index, because
        /// File.WriteAllTextAsync truncates the target first: a crash or a concurrent
        /// reader in that window saw an empty or half-written projects.json, and the
        /// project list came back empty. File.Move with overwrite is atomic on NTFS.
        /// </summary>
        private async Task WriteAllProjects_NoLock(List<ProjectInfo> projects)
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

            string tempPath = _projectsListPath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, _projectsListPath, overwrite: true);
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

                // Сохраняем мигрированные проекты. Вызывается только из
                // ReadAllProjects_NoLock, то есть гейт уже удерживается.
                if (migratedProjects.Count > 0)
                {
                    await WriteAllProjects_NoLock(migratedProjects);
                }
            }
            catch
            {
                // Игнорируем ошибки миграции
            }
        }
    }
}
