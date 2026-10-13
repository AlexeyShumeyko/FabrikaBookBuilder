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
using PhotoBookRenamer.Presentation.Views;
using PhotoBookRenamer.Presentation.Converters;
using PhotoBookRenamer.Presentation.Dialogs;

namespace PhotoBookRenamer.Presentation.ViewModels
{
    /// <summary>Taking a project in, preparing it, and handing it back.</summary>
    public partial class CombinedModeViewModel : ViewModelBase, IProjectEditor
    {

        public async void SetProject(Project? project, ProjectInfo projectInfo)
        {
            Books.Clear();
            Project = null;
            AvailableFiles.Clear();
            IsStructureConfirmed = false;
            ErrorMessage = null;
            Presentation.Converters.PageSourceConverter.ClearCache();
            Presentation.Converters.FilePathToThumbnailConverter.ClearCache();

            var projectId = projectInfo.Id ?? string.Empty;
            var projectName = projectInfo.Name ?? string.Empty;
            var projectFilePath = projectInfo.FilePath ?? string.Empty;
            var projectMode = projectInfo.Mode;

            if (string.IsNullOrEmpty(projectId))
            {
                projectId = Guid.NewGuid().ToString();
            }

            var projectInfoCopy = new ProjectInfo
            {
                Id = projectId,
                Name = projectName,
                FilePath = projectFilePath,
                Mode = projectMode,
                BookCount = projectInfo.BookCount,
                PageCount = projectInfo.PageCount,
                Status = projectInfo.Status,
                CreatedDate = projectInfo.CreatedDate,
                LastModified = projectInfo.LastModified
            };

            CurrentProjectInfo = projectInfoCopy;
            ProjectName = projectInfoCopy.Name;

            // Off unless FBR_PERF_TRACE=1, and then it only writes lines. It is here because
            // every guess about this cost was wrong - see PerfPhase for the numbers.
            PerfPhase.Reset();

            if (project == null && !string.IsNullOrEmpty(projectFilePath) && File.Exists(projectFilePath))
            {
                project = await _projects.LoadAsync(projectFilePath);
            }

            if (project == null)
            {
                project = new Project { Mode = AppMode.Combined };
            }
            else
            {
                project.Mode = AppMode.Combined;

                if (project.AvailableFiles != null)
                {
                    foreach (var file in project.AvailableFiles)
                    {
                        if (File.Exists(file))
                        {
                            AvailableFiles.Add(file);
                        }
                    }
                }

                if (project.Books != null && project.Books.Count > 0)
                {
                    NumberOfBooks = project.Books.Count;
                    SpreadsPerBook = project.Books.FirstOrDefault()?.Pages?.Count ?? 1;
                    IsStructureConfirmed = true;

                    Books.Clear();
                    // Re-decide from what is on disk: the first spread of the saved run
                    // sets the shape again, so a reopened project looks exactly like it did
                    // when it was closed.
                    foreach (var book in project.Books)
                    {
                        Books.Add(book);
                    }

                    foreach (var book in Books)
                    {
                        // One helper owns the thumbnail's path now. It used to be rebuilt
                        // inline here, once per mode, which is three copies of one formula.
                        if (book.Cover != null)
                            book.Cover.ThumbnailPath = _thumbnails.GetExistingPath(book.Cover.SourcePath ?? string.Empty);

                        foreach (var page in book.Pages)
                        {
                            if (page != null)
                                page.ThumbnailPath = _thumbnails.GetExistingPath(page.SourcePath ?? string.Empty);
                        }
                    }

                    // A photo with no thumbnail yet has to decode its original - and these
                    // originals are 18 MB print files. Doing that inside the layout pass
                    // freezes the window and leaves slots painted as empty grey boxes, which
                    // is what we saw on a first open. The pass below builds the
                    // missing thumbnails off the UI thread, hands the decoded bitmaps to the
                    // converter's cache, and then tells the slots to look again.
                }
            }

            Project = project;

            if (Project != null)
            {
                Project.Books.Clear();
                foreach (var book in Books)
                {
                    Project.Books.Add(book);
                }

                if (CurrentProjectInfo != null)
                {
                    CurrentProjectInfo.PageCount = Project.Books?.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                    CurrentProjectInfo.BookCount = Project.Books?.Count ?? 0;
                    Background.Run(
                        async () => await _projectListService.SaveProjectInfoAsync(CurrentProjectInfo),
                        "save project index");
                }
            }

            OnPropertyChanged(nameof(Books));
            OnPropertyChanged(nameof(Project));
            OnPropertyChanged(nameof(ProjectName));

            // The ViewModel is not the cost: the layout of the slots it just announced is,
            // and that happens after this method returns, inside the next dispatcher pass.
            PerfPhase.Mark("view model done (the layout pass is still to come)");
            var app = System.Windows.Application.Current;
            if (app != null)
            {
                app.Dispatcher.BeginInvoke(new Action(() => PerfPhase.CountElements("at Render priority (layout done)")), System.Windows.Threading.DispatcherPriority.Render);
                app.Dispatcher.BeginInvoke(new Action(() => PerfPhase.CountElements("at Background priority")), System.Windows.Threading.DispatcherPriority.Background);
                app.Dispatcher.BeginInvoke(new Action(() => { PerfPhase.CountElements("at ContextIdle (dispatcher empty)"); PerfPhase.Write("open combined project"); }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }

            // One preparation for this load, shared by both passes below. A second token
            // would cancel the first one the moment it starts, and the prewarm of a project
            // that also has loose files would never get past the first photograph.
            var preparation = BeginPreparation();
            if (Project?.Books != null && Project.Books.Any())
            {
                var allImagePaths = Project.Books
                    .SelectMany(b => b.Pages.Select(p => p.SourcePath).Concat(new[] { b.Cover?.SourcePath }))
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();

                if (allImagePaths.Any())
                {
                    Background.Run(() => PrewarmAndWarmThumbnailsAsync(allImagePaths!, preparation), "prewarm thumbnails");
                }
            }
            if (AvailableFiles.Any())
            {
                Background.Run(async () => await _thumbnails.EnsureAsync(AvailableFiles, cancellationToken: preparation), "load thumbnails");
            }

            UpdateExportCommands();
        }

        /// <summary>
        /// Gets every photo of a freshly opened project ready to draw, in this order and off
        /// the UI thread:
        ///
        ///   1. build the thumbnails that do not exist yet;
        ///   2. decode what is left into the converter's cache;
        ///   3. point the slots at the new thumbnails and tell the view to look again.
        ///
        /// The order matters. Step 2 is what keeps the UI thread out of an 18 MB JPEG decode
        /// during the first layout, which is the difference between "the photos are there"
        /// and "the photo cells are empty grey boxes until I save and reopen". Step 3 is what
        /// makes the result visible: a slot only re-reads its photo when something raises a
        /// notification.
        ///
        /// Nothing here can lose work: it only adds thumbnails and bitmap cache entries, and
        /// every step is individually wrapped so a failure leaves the previous behaviour.
        /// </summary>
        private async Task PrewarmAndWarmThumbnailsAsync(List<string> imagePaths, CancellationToken preparation)
        {
            try
            {
                await _thumbnails.EnsureAsync(imagePaths, cancellationToken: preparation);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Не удалось построить миниатюры при открытии проекта", ex);
            }

            try
            {
                await Presentation.Converters.PageSourceConverter.PrewarmAsync(imagePaths, preparation);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Не удалось подготовить фото при открытии проекта", ex);
            }

            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var book in Books)
                    {
                        if (book.Cover != null && !string.IsNullOrEmpty(book.Cover.SourcePath))
                        {
                            var thumb = _thumbnails.GetExistingPath(book.Cover.SourcePath!);
                            if (thumb != null && book.Cover.ThumbnailPath != thumb)
                            {
                                book.Cover.ThumbnailPath = thumb;
                                book.Cover.RaiseThumbnailChanged();
                            }
                        }

                        foreach (var page in book.Pages.Where(p => p != null && !string.IsNullOrEmpty(p.SourcePath)))
                        {
                            var thumb = _thumbnails.GetExistingPath(page.SourcePath!);
                            if (thumb != null && page.ThumbnailPath != thumb)
                            {
                                page.ThumbnailPath = thumb;
                                page.RaiseThumbnailChanged();
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Не удалось обновить миниатюры слотов", ex);
            }
        }

        private async Task SaveProjectAsync()
        {
            // The project record is created HERE rather than on the first "load photos"
            // click. It used to be created by the file loader only, and the save button's
            // CanExecute was CurrentProjectInfo != null - so a photo dropped straight into
            // an empty slot left the button dead, while the same photo loaded through the
            // loader panel worked. The user must not have to know which door the photo
            // came in by.
            if (await EnsureProjectInfoAsync() == null)
            {
                System.Windows.MessageBox.Show("Не удалось создать запись проекта.",
                    "Ошибка", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            // A project is created before its files arrive; this is where it starts.
            if (Project == null)
            {
                Project = new Project { Mode = AppMode.Combined };
            }

            try
            {
                IsLoading = true;

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

                var updatedInfo = await _projectListService.UpdateProjectInfoAsync(Project, filePath);
                if (updatedInfo != null)
                {
                    var savedName = CurrentProjectInfo.Name;
                    CurrentProjectInfo.BookCount = updatedInfo.BookCount;
                    CurrentProjectInfo.PageCount = updatedInfo.PageCount;
                    CurrentProjectInfo.Status = updatedInfo.Status;
                    CurrentProjectInfo.LastModified = updatedInfo.LastModified;
                    CurrentProjectInfo.Name = savedName;
                }

                await _projectListService.SaveProjectInfoAsync(CurrentProjectInfo);
                RequestBackToProjectList();
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Ошибка сохранения проекта", ex);
                System.Windows.MessageBox.Show($"Ошибка сохранения проекта: {ex.Message}",
                    "Ошибка", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private ProjectStatus DetermineStatus(Project project)
        {
            if (project == null || project.Books == null || project.Books.Count == 0)
            {
                return ProjectStatus.NotFilled;
            }

            var allFilled = project.Books.All(book =>
                book.Cover != null && !book.Cover.IsEmpty &&
                book.Pages != null && book.Pages.All(p => !p.IsEmpty));

            return allFilled ? ProjectStatus.Ready : ProjectStatus.NotFilled;
        }

        private async Task BackAsync()
        {
            if (CurrentProjectInfo != null && Project != null &&
                (Books.Count > 0 || AvailableFiles.Count > 0))
            {
                // Save / discard / stay. Kept as three buttons rather than a two-button
                // box: "не сохранять" throws work away and "отмена" keeps the user on the
                // screen, and neither can be inferred from the other.
                var result = Presentation.Dialogs.AppDialogs.Ask(
                    "Сохранить перед выходом?",
                    "В проекте есть несохранённые изменения. Сохранить их перед возвратом к списку проектов?",
                    "Сохранить",
                    "Не сохранять");

                if (result == Presentation.Dialogs.ConfirmOutcome.Primary)
                {
                    await SaveProjectAsync();
                    return;
                }
                else if (result == Presentation.Dialogs.ConfirmOutcome.Cancelled)
                {
                    return;
                }
            }

            if (CurrentProjectInfo != null &&
                (Books.Count == 0 && AvailableFiles.Count == 0))
            {
                try
                {
                    await _projectListService.DeleteProjectAsync(CurrentProjectInfo);
                }
                catch (Exception ex)
                {
                }
            }

            RequestBackToProjectList();
        }
    }
}
