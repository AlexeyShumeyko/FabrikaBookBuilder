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
using PhotoBook.Infrastructure;
using PhotoBookRenamer.Presentation.Dialogs;

namespace PhotoBookRenamer.Presentation.ViewModels
{
    /// <summary>Putting the project on disk, loudly or quietly.</summary>
    public partial class CombinedModeViewModel : ViewModelBase, IProjectEditor
    {

        /// <summary>
        /// Saves the project right before an export, and never lets a save problem stop the
        /// export. The copy is what we asked for; the save is the safety net under
        /// it, so a full disk or a locked index must not turn into "nothing happened".
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
                ErrorMessage = "Не удалось сохранить проект перед экспортом. Экспорт продолжен, но проект может быть потерян.";
                OnPropertyChanged(nameof(ErrorMessage));
            }
        }

        private async Task SaveProjectSilentlyAsync()
        {
            if (CurrentProjectInfo == null)
            {
                return;
            }

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

                if (!string.IsNullOrEmpty(ProjectName) && ProjectName != CurrentProjectInfo.Name)
                {
                    CurrentProjectInfo.Name = ProjectName;
                }

                // Books must mirror Project.Books: the view binds to Books, the save walks Project.Books.
                Project.Books.Clear();
                foreach (var book in Books)
                {
                    Project.Books.Add(book);
                }

                Project.AvailableFiles.Clear();
                foreach (var file in AvailableFiles)
                {
                    Project.AvailableFiles.Add(file);
                }

                var projectsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhotoBookRenamer",
                    "Projects");

                if (!Directory.Exists(projectsDir))
                {
                    Directory.CreateDirectory(projectsDir);
                }

                var filePath = Path.Combine(projectsDir, $"{projectId}.json");
                await _projects.SaveAsync(Project, filePath);

                if (!string.IsNullOrEmpty(ProjectName))
                {
                    CurrentProjectInfo.Name = ProjectName;
                }

                CurrentProjectInfo.FilePath = filePath;
                CurrentProjectInfo.BookCount = Books.Count;
                CurrentProjectInfo.PageCount = Books.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                CurrentProjectInfo.Status = DetermineStatus(Project);
                CurrentProjectInfo.LastModified = DateTime.Now;

                await _projectListService.UpdateProjectInfoAsync(Project, filePath);
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

            var info = await _projectListService.CreateProjectAsync(AppMode.Combined, name);
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
            if (Books.Count == 0 && AvailableFiles.Count == 0)
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

        /// <summary>
        /// Adds a single file that was dragged in from Windows Explorer.
        /// Returns false for unsupported formats or duplicates.
        ///
        /// The original drop handler was empty, so dragging photos from Explorer did
        /// nothing; the redesigned drop zone wires it up.
        /// </summary>
        public bool AddExternalFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
            if (AvailableFiles.Contains(filePath)) return false;
            if (!_fileService.IsJpegFile(filePath)) return false;

            AvailableFiles.Add(filePath);
            return true;
        }

        /// <summary>Kicks off thumbnail generation for pool items added outside the picker.</summary>
        public void LoadThumbnailsForNewFiles()
        {
            if (AvailableFiles.Count == 0) return;

            var paths = AvailableFiles.ToList();
            var preparation = BeginPreparation();
            Background.Run(async () =>
            {
                await _thumbnails.EnsureAsync(paths, cancellationToken: preparation);
                _images.InvalidateAll();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var p in Books)
                    {
                        if (p.Cover != null) p.Cover.RaiseThumbnailChanged();
                        foreach (var page in p.Pages) page.RaiseThumbnailChanged();
                    }
                });
            }, "load thumbnails for new files");
        }
    }

    public enum DropAction
    {
        ThisBookOnly,
        AllBooks,
        SelectedBooks
    }
}
