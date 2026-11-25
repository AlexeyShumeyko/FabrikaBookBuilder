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
using PhotoBook.Core;
using PhotoBook.Application;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>Putting the project on disk, loudly or quietly.</summary>
    public partial class UniqueFoldersViewModel : ViewModelBase, IProjectEditor
    {
        /// <summary>Whether the last write reached the disk. Drives what a flush answers.</summary>
        private bool _lastSaveSucceeded = true;

        /// <summary>
        /// Asks for the project to be written once the current gesture has finished. Every
        /// change a photographer can make raises something this listens to.
        /// </summary>
        private void RequestAutoSave()
        {
            if (_suppressAutoSave) return;
            _autoSave.Request();
        }

        /// <summary>
        /// What the autosave writes. The record is created on demand, so a run assembled
        /// from folders gets a card in the list without anybody pressing anything.
        /// </summary>
        private async Task AutosaveNowAsync()
        {
            if (Project == null || Project.Books == null || Project.Books.Count == 0) return;

            if (await EnsureProjectInfoAsync() == null)
            {
                _lastSaveSucceeded = false;
                ErrorMessage = "Не удалось создать запись проекта.";
                return;
            }

            await SaveProjectSilentlyAsync();
        }

        /// <summary>
        /// Watches a book's slots. A photograph going in, a spread leaving or a cover being
        /// replaced all reach the screen as one of these three notifications, and each of
        /// them is a change worth keeping.
        /// </summary>
        private void WatchBook(Book book)
        {
            if (_bookWatchers.ContainsKey(book)) return;

            System.ComponentModel.PropertyChangedEventHandler handler = (_, e) =>
            {
                if (e.PropertyName is nameof(Book.Cover)
                                     or nameof(Book.Pages)
                                     or nameof(Book.AllSlotsPages))
                {
                    RequestAutoSave();
                }
            };

            _bookWatchers[book] = handler;
            book.PropertyChanged += handler;
        }

        private void UnwatchBook(Book book)
        {
            if (!_bookWatchers.TryGetValue(book, out var handler)) return;

            book.PropertyChanged -= handler;
            _bookWatchers.Remove(book);
        }

        /// <summary>
        /// Returns to the project list. Everything is saved as it is made, so there is no
        /// question to ask; the save here only makes sure the last gesture reached the disk.
        /// </summary>
        private async Task BackAsync()
        {
            bool hasWork = Project?.Books is { Count: > 0 };

            if (hasWork && !await QuickSaveAsync())
            {
                // The screen stays: leaving with the project unwritten would lose the run,
                // and the error message already says what to do about it.
                return;
            }

            if (!hasWork) _autoSave.Cancel();

            if (CurrentProjectInfo != null)
            {
                var wasSaved = !string.IsNullOrEmpty(CurrentProjectInfo.FilePath) &&
                               System.IO.File.Exists(CurrentProjectInfo.FilePath);

                // Nothing was saved and there is nothing in it, so it goes: an empty
                // project in the list is a card that opens onto nothing.
                if (!wasSaved && (Project == null || Project.Books == null || Project.Books.Count == 0))
                {
                    await _projectListService.DeleteProjectAsync(CurrentProjectInfo);
                }
            }

            RequestBackToProjectList();
        }

        private async Task LoadThumbnailForPage(Page page)
        {
            if (string.IsNullOrEmpty(page.SourcePath)) return;

            var thumbPath = ThumbnailStore.GetPath(page.SourcePath);

            page.ThumbnailPath = await _thumbnails.GetOrCreateAsync(page.SourcePath) ?? thumbPath;
        }

        /// <summary>
        /// Saves the project right before an export, and never lets a save problem stop the
        /// export: the copy is what we asked for, the save is the safety net under
        /// it, so a full disk must not turn into "nothing happened".
        /// </summary>
        private async Task SaveProjectBeforeExportAsync()
        {
            try
            {
                await QuickSaveAsync();
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Не удалось сохранить проект перед экспортом", ex);
            }
        }

        private async Task<bool> SaveProjectSilentlyAsync()
        {
            var info = CurrentProjectInfo;
            var project = Project;

            if (info == null || project == null)
            {
                return false;
            }

            // Captured before the first await: a different project can be opened while this
            // write waits for the disk, and the fields must not redirect it.
            var projectName = ProjectName;

            try
            {
                var projectId = info.Id ?? string.Empty;
                if (string.IsNullOrEmpty(projectId))
                {
                    projectId = Guid.NewGuid().ToString();
                    info.Id = projectId;
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

                await _projects.SaveAsync(project, projectFilePath);

                info.FilePath = projectFilePath;
                info.Name = projectName ?? info.Name;
                info.BookCount = project.Books?.Count ?? 0;
                info.PageCount = project.Books?.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                info.Status = DetermineProjectStatus(project);
                info.LastModified = DateTime.Now;

                await _projectListService.SaveProjectInfoAsync(info);

                _lastSaveSucceeded = true;
                return true;
            }
            catch (Exception ex)
            {
                _lastSaveSucceeded = false;
                _loggingService.LogError("Ошибка автоматического сохранения проекта", ex);
                ErrorMessage = "Не удалось сохранить проект. Проверьте свободное место на диске.";
                return false;
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
        /// A save that does not navigate away, and the one the shell calls before leaving a
        /// screen. Pending automatic work is written first, so leaving cannot outrun it.
        /// </summary>
        public async Task<bool> QuickSaveAsync()
        {
            if (Project == null || Project.Books == null || Project.Books.Count == 0)
            {
                _autoSave.Cancel();
                return false;
            }

            if (await EnsureProjectInfoAsync() == null)
            {
                ErrorMessage = "Не удалось создать запись проекта.";
                return false;
            }

            if (await _autoSave.FlushAsync())
            {
                return _lastSaveSucceeded;
            }

            return await SaveProjectSilentlyAsync();
        }
    }
}
