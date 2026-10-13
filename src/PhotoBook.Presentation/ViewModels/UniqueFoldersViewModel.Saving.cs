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

                await _projects.SaveAsync(Project, projectFilePath);

                if (CurrentProjectInfo != null)
                {
                    CurrentProjectInfo.FilePath = projectFilePath;
                    CurrentProjectInfo.Name = ProjectName ?? CurrentProjectInfo.Name;
                    CurrentProjectInfo.BookCount = Project.Books?.Count ?? 0;
                    CurrentProjectInfo.PageCount = Project.Books?.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                    CurrentProjectInfo.Status = DetermineProjectStatus(Project);
                    CurrentProjectInfo.LastModified = DateTime.Now;
                }

                {
                    await _projectListService.SaveProjectInfoAsync(CurrentProjectInfo);
                }
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
