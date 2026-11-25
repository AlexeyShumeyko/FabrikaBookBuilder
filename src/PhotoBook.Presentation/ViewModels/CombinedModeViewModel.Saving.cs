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
using PhotoBook.Presentation;
using PhotoBook.Application;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>Putting the project on disk, loudly or quietly.</summary>
    public partial class CombinedModeViewModel : ViewModelBase, IProjectEditor
    {
        /// <summary>Whether the last write reached the disk. Drives what a flush answers.</summary>
        private bool _lastSaveSucceeded = true;

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

        /// <summary>
        /// Asks for the project to be written once the current gesture has finished. This is
        /// the whole save story of the mode: there is no button, and nothing a user can do to
        /// a photographed or a spread is left out.
        /// </summary>
        private void RequestAutoSave()
        {
            if (_suppressAutoSave) return;
            _autoSave.Request();
        }

        /// <summary>
        /// What the autosave writes. The record is created on demand, exactly as the removed
        /// button used to do it, so a project assembled entirely with the mouse still gets a
        /// card in the list.
        /// </summary>
        private async Task AutosaveNowAsync()
        {
            if (Books.Count == 0 && AvailableFiles.Count == 0) return;

            if (await EnsureProjectInfoAsync() == null)
            {
                _lastSaveSucceeded = false;
                ErrorMessage = "Не удалось создать запись проекта.";
                return;
            }

            await SaveProjectSilentlyAsync();
        }

        private async Task<bool> SaveProjectSilentlyAsync()
        {
            var info = CurrentProjectInfo;
            if (info == null)
            {
                return false;
            }

            if (Project == null)
            {
                Project = new Project { Mode = AppMode.Combined };
            }

            // Both are captured before the first await. A different project can be opened
            // while this write is waiting for the disk; re-reading the fields afterwards
            // would put the new project's contents into the old project's file.
            var project = Project;
            var projectName = ProjectName;

            try
            {
                var projectId = info.Id ?? string.Empty;
                if (string.IsNullOrEmpty(projectId))
                {
                    projectId = Guid.NewGuid().ToString();
                    info.Id = projectId;
                }

                if (!string.IsNullOrEmpty(projectName) && projectName != info.Name)
                {
                    info.Name = projectName;
                }

                // Books must mirror Project.Books: the view binds to Books, the save walks Project.Books.
                project.Books.Clear();
                foreach (var book in Books)
                {
                    project.Books.Add(book);
                }

                project.AvailableFiles.Clear();
                foreach (var file in AvailableFiles)
                {
                    project.AvailableFiles.Add(file);
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
                await _projects.SaveAsync(project, filePath);

                if (!string.IsNullOrEmpty(projectName))
                {
                    info.Name = projectName;
                }

                info.FilePath = filePath;
                info.BookCount = project.Books.Count;
                info.PageCount = project.Books.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                info.Status = DetermineStatus(project);
                info.LastModified = DateTime.Now;

                await _projectListService.UpdateProjectInfoAsync(project, filePath);
                _lastSaveSucceeded = true;
                return true;
            }
            catch (Exception ex)
            {
                _lastSaveSucceeded = false;
                _loggingService.LogError("Ошибка автоматического сохранения проекта", ex);

                // The work is still on screen, and the next change asks again, so this is
                // information rather than an ending. Silence, on the other hand, is how a
                // customer keeps working in a project that is not being written anywhere.
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

            var info = await _projectListService.CreateProjectAsync(AppMode.Combined, name);
            if (info == null) return null;

            CurrentProjectInfo = info;
            OnPropertyChanged(nameof(CurrentProjectInfo));
            return info;
        }

        /// <summary>
        /// A save that does not navigate away, and the one the shell calls before leaving a
        /// screen. Pending automatic work is written first, so leaving cannot outrun it; a
        /// write that was already running is waited for rather than raced with.
        /// </summary>
        public async Task<bool> QuickSaveAsync()
        {
            if (Books.Count == 0 && AvailableFiles.Count == 0)
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
                // The flush wrote through AutosaveNowAsync; a failure there is already on
                // screen, and answering "saved" would let the caller leave the project.
                return _lastSaveSucceeded;
            }

            return await SaveProjectSilentlyAsync();
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
                await Ui.InvokeAsync(() =>
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
