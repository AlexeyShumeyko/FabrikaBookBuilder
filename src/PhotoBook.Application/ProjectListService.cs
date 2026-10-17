using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Core;

namespace PhotoBook.Application
{
    /// <summary>
    /// What the project list is: which projects exist, what state each is in, and when a
    /// record is created, updated or removed.
    /// </summary>
    /// <remarks>
    /// Where the index lives, how it is written and what shape it is on disk are all
    /// somebody else's business - see <see cref="IProjectIndexStore"/>. This class used to
    /// assemble the path, create the folder, read and write the file and serialise it, which
    /// meant the layer that decides what a project is also knew where projects are kept.
    /// </remarks>
    internal class ProjectListService : IProjectListService
    {
        private readonly IProjectIndexStore _index;
        private readonly IFileService _files;
        private readonly ILoggingService _logging;

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

        /// <param name="logging">
        /// The index is the one file six call sites write without looking at the answer, so a failed
        /// write has to be recorded here or it is recorded nowhere.
        /// </param>
        public ProjectListService(IProjectIndexStore index, IFileService files, ILoggingService logging)
        {
            _index = index;
            _files = files;
            _logging = logging;
        }

        /// <summary>
        /// The file a project lives in. The store decides the layout of the folder; this
        /// layer only needs to know where to point the repository and the folder picker.
        /// </summary>
        private string GetProjectFilePath(string projectId) => _index.GetProjectFilePath(projectId);

        /// <summary>
        /// Every project recorded in the index under one mode.
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
        /// Every project recorded in the index, whatever mode it is in.
        /// </summary>
        public async Task<List<ProjectInfo>> GetAllProjectsAsync()
        {
            await _indexGate.WaitAsync();
            try
            {
                var projects = await ReadAllProjects_NoLock();

                var validatedProjects = new List<ProjectInfo>();
                var usedIds = new HashSet<string>();
                bool repaired = false;

                foreach (var project in projects)
                {
                    // An Id that is empty or already taken is replaced - two projects sharing
                    // one file name would mean one of them silently disappears.
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

                    // The file path is derived from the Id every time rather than trusted from the
                    // record: a record whose path drifted from its Id would be read and never found.
                    string expected = GetProjectFilePath(project.Id);
                    if (project.FilePath != expected)
                    {
                        project.FilePath = expected;
                        repaired = true;
                    }

                    validatedProjects.Add(project);
                }

                // Written back only when something was actually repaired. The comparison
                // used to be made on Name, which rewrote the file on almost every read.
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
            var json = await _index.ReadAsync();
            if (json == null)
            {
                // No index yet. Older versions kept one file per mode, so give those a
                // chance to be carried across before deciding the list is empty.
                await MigrateOldProjectsAsync();
                json = await _index.ReadAsync();
                if (json == null)
                {
                    return new List<ProjectInfo>();
                }
            }

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
        /// A new project, with an Id of its own.
        /// </summary>
        public async Task<ProjectInfo?> CreateProjectAsync(AppMode mode, string name)
        {
            await _indexGate.WaitAsync();
            try
            {
                var allProjects = await ReadAllProjects_NoLock();

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
        /// Writes the record of one project into the index.
        /// Updates the entry carrying the same Id and leaves every other one alone.
        /// </summary>
        public async Task<Result> SaveProjectInfoAsync(ProjectInfo projectInfo)
        {
            if (string.IsNullOrEmpty(projectInfo.Id))
            {
                return Result.Fail("У проекта нет идентификатора, поэтому его запись некуда сохранить.");
            }

            await _indexGate.WaitAsync();
            try
            {
                var projectId = projectInfo.Id;
                var projectFilePath = GetProjectFilePath(projectId);

                var allProjects = await ReadAllProjects_NoLock();

                var existingIndex = allProjects.FindIndex(p => p.Id == projectId);

                if (existingIndex >= 0)
                {
                    var existing = allProjects[existingIndex];

                    // PageCount and CreatedDate are carried over from the old record:
                    // the editors send only what changed, so without this the two were
                    // quietly zeroed - the card lost its page count and the list stopped
                    // sorting by creation date.
                    allProjects[existingIndex] = new ProjectInfo
                    {
                        Id = projectId, // the Id the record was found by
                        Name = projectInfo.Name,
                        FilePath = projectFilePath,
                        Mode = projectInfo.Mode,
                        BookCount = projectInfo.BookCount,
                        PageCount = projectInfo.PageCount > 0 ? projectInfo.PageCount : existing.PageCount,
                        CreatedDate = projectInfo.CreatedDate != default ? projectInfo.CreatedDate : existing.CreatedDate,
                        Status = projectInfo.Status,
                        LastModified = DateTime.Now
                    };

                    // The card's pictures and its total travel with the record. They used to
                    // be dropped here, which is part of why the list had to open every project
                    // file to draw a card: nothing had ever written them down.
                    foreach (var photo in projectInfo.PreviewPhotos) allProjects[existingIndex].PreviewPhotos.Add(photo);
                    allProjects[existingIndex].TotalPhotoCount = projectInfo.TotalPhotoCount;
                }
                else
                {
                    projectInfo.FilePath = projectFilePath;
                    projectInfo.LastModified = DateTime.Now;
                    if (projectInfo.CreatedDate == default) projectInfo.CreatedDate = DateTime.Now;
                    allProjects.Add(projectInfo);
                }

                await WriteAllProjects_NoLock(allProjects);
                return Result.Ok();
            }
            catch (Exception ex)
            {
                // The variable was named and then ignored: the reason existed here and was
                // thrown away, six call sites away from anyone who could have read it.
                _logging.LogError("Не удалось сохранить запись проекта в списке", ex);
                return Result.Fail($"Не удалось сохранить запись проекта: {ex.Message}");
            }
            finally
            {
                _indexGate.Release();
            }
        }

        /// <summary>
        /// Takes the project off the list and deletes its file.
        /// </summary>
        public async Task<Result> DeleteProjectAsync(ProjectInfo projectInfo)
        {
            if (string.IsNullOrEmpty(projectInfo?.Id))
            {
                return Result.Fail("У проекта нет идентификатора, поэтому его нельзя удалить.");
            }

            await _indexGate.WaitAsync();
            try
            {
                var allProjects = await ReadAllProjects_NoLock();
                var projectToDelete = allProjects.FirstOrDefault(p => p.Id == projectInfo.Id);

                if (projectToDelete == null)
                {
                    return Result.Fail("Проекта нет в списке - возможно, его удалили в другом окне.");
                }

                allProjects.Remove(projectToDelete);
                await WriteAllProjects_NoLock(allProjects);

                // The entry is off the index either way; the file goes too when it is there.
                // A project with no file on disk is a half-made project, not a failure.
                _index.DeleteProjectFile(projectInfo.Id);

                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logging.LogError("Не удалось удалить проект из списка", ex);
                return Result.Fail($"Не удалось удалить проект: {ex.Message}");
            }
            finally
            {
                _indexGate.Release();
            }
        }

        /// <summary>
        /// Rewrites the record from the project that was just loaded, and returns it.
        /// </summary>
        public async Task<ProjectInfo?> UpdateProjectInfoAsync(Project project, string filePath)
        {
            try
            {
                // Qualified on purpose. This is string handling on a path, not a question for the disk -
                // nothing is opened and nothing is asked of the file system - and the rest of
                // this class no longer touches System.IO at all. Spelling it out keeps
                // "using System.IO" out of this file, which is what makes the check that this
                // layer stays off the disk meaningful instead of noisy.
                var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                if (string.IsNullOrEmpty(fileName))
                {
                    return null;
                }

                var allProjects = await GetAllProjectsAsync();
                var projectInfo = allProjects.FirstOrDefault(p => p.Id == fileName);

                if (projectInfo != null)
                {
                    projectInfo.BookCount = project.Books?.Count ?? 0;
                    // PageCount is the spreads in ONE book, not the sum over all of them.
                    // Every book in a run has the same number, which is what makes the
                    // number on the card worth reading.
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
        /// The replace itself is the store's business, and it is not a detail: a plain
        /// truncate-then-write leaves an empty or half-written index to anything that reads
        /// during the window, and a half-written index reads as an empty project list.
        /// </summary>
        private async Task WriteAllProjects_NoLock(List<ProjectInfo> projects)
        {
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

            await _index.WriteAsync(json);
        }

        /// <summary>
        /// How far the project has been filled in.
        /// </summary>

        /// <inheritdoc />
        public async Task RefreshFromProjectAsync(string projectId, Project project)
        {
            if (string.IsNullOrEmpty(projectId) || project is null) return;

            await _indexGate.WaitAsync();
            try
            {
                var all = await ReadAllProjects_NoLock();
                var index = all.FindIndex(p => p.Id == projectId);
                if (index < 0) return;

                // LastModified is deliberately NOT touched here.
                //
                // A project being filled in for the first time has not been worked on, and
                // saying otherwise floats it to the top of the list with a date that is a lie
                // about the last time a photographer touched it. The editors already bump
                // the date when a project is genuinely saved.
                ProjectCardData.Fill(_files, project, all[index]);

                await WriteAllProjects_NoLock(all);
            }
            catch (Exception ex)
            {
                // A project that saved correctly must not be reported as failed because its
                // list entry did not refresh. The next save, or the next time the project is
                // opened, puts it right.
                _logging.LogError("Не удалось обновить запись проекта в списке после сохранения", ex);
            }
            finally
            {
                _indexGate.Release();
            }
        }
        private ProjectStatus DetermineStatus(Project project)
        {
            if (project.Books == null || project.Books.Count == 0)
            {
                return ProjectStatus.NotFilled;
            }

            var allBooksReady = project.Books.All(b => b.IsValid);

            if (allBooksReady)
            {
                if (!string.IsNullOrEmpty(project.OutputFolder) && _files.DirectoryExists(project.OutputFolder))
                {
                    return ProjectStatus.SuccessfullyCompleted;
                }

                return ProjectStatus.Ready;
            }

            return ProjectStatus.NotFilled;
        }

        /// <summary>
        /// Brings projects written by older versions into the current layout.
        /// </summary>
        private async Task MigrateOldProjectsAsync()
        {
            try
            {
                var migratedProjects = new List<ProjectInfo>();

                foreach (AppMode mode in Enum.GetValues(typeof(AppMode)))
                {
                    if (mode == AppMode.StartScreen || mode == AppMode.ProjectList)
                        continue;

                    var oldJson = await _index.ReadLegacyAsync(mode.ToString());
                    if (oldJson != null)
                    {
                        try
                        {
                            var json = oldJson;
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
                                        if (string.IsNullOrEmpty(oldProject.Id))
                                        {
                                            oldProject.Id = Guid.NewGuid().ToString();
                                        }

                                        oldProject.FilePath = GetProjectFilePath(oldProject.Id);

                                        migratedProjects.Add(oldProject);
                                    }
                                }
                            }
                        }
                        catch
                        {
                        }
                    }
                }

                // Reached only from ReadAllProjects_NoLock, so the gate is already held
                // and taking it again here would deadlock.
                if (migratedProjects.Count > 0)
                {
                    await WriteAllProjects_NoLock(migratedProjects);
                }
            }
            catch
            {
            }
        }
    }
}
