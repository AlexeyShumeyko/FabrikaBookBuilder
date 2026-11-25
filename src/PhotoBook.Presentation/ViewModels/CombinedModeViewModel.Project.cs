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
    /// <summary>Taking a project in, preparing it, and handing it back.</summary>
    public partial class CombinedModeViewModel : ViewModelBase, IProjectEditor
    {

        public async void SetProject(Project? project, ProjectInfo projectInfo)
        {
            // Loading raises every notification a real edit raises. Saving during that pass
            // would rewrite the project that is being opened, with half of it already in place.
            _autoSave.Cancel();
            _suppressAutoSave = true;
            try
            {
                await SetProjectCoreAsync(project, projectInfo);
            }
            finally
            {
                _suppressAutoSave = false;
            }
        }

        private async Task SetProjectCoreAsync(Project? project, ProjectInfo projectInfo)
        {
            Books.Clear();
            Project = null;
            AvailableFiles.Clear();
            IsStructureConfirmed = false;
            ErrorMessage = null;
            _images.InvalidateAll();

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

                    PerfPhase.Mark("books populated");

                    // The paths are already on the pages: the repository attaches them while
                    // the project is being read, off this thread. Doing it here put a thousand
                    // property notifications on the interface thread on the way in, measured
                    // at 2.6 s for a fifty-book run, and the screen waited for all of it.

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

            // The three priorities are the measurement, not decoration: they are the three
            // moments where "how much has been built" is a different question. Asking for
            // them by name - rather than by the engine's dispatcher - is what lets a second
            // shell answer the same three questions on its own schedule.
            Ui.BeginInvoke(
                () => PerfPhase.CountElements("at Render priority (layout done)"),
                UiDispatchPriority.Render);
            Ui.BeginInvoke(
                () => PerfPhase.CountElements("at Background priority"),
                UiDispatchPriority.Background);
            Ui.BeginInvoke(
                () =>
                {
                    PerfPhase.CountElements("at ContextIdle (dispatcher empty)");
                    PerfPhase.Write("open combined project");
                },
                UiDispatchPriority.ContextIdle);

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
            PerfPhase.Mark("thumbnail warming started");

            try
            {
                await _thumbnails.EnsureAsync(imagePaths, cancellationToken: preparation);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Не удалось построить миниатюры при открытии проекта", ex);
            }

            PerfPhase.Mark("thumbnails built");

            try
            {
                await _images.PrewarmAsync(imagePaths, preparation);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Не удалось подготовить фото при открытии проекта", ex);
            }

            PerfPhase.Mark("decoded images prepared");

            try
            {
                Ui.Invoke(() =>
                {
                    foreach (var book in Books)
                    {
                        // Only pages without a copy are looked up, and only an assignment is
                        // announced - the setter raises ThumbnailPath, which is bound in the
                        // slot card. The path attached when the project was read is an
                        // existing copy, so a page that already has one is already drawn.
                        // Announcing all thousand pages re-evaluated every visible binding
                        // for a value that had not moved.
                        if (book.Cover != null && !string.IsNullOrEmpty(book.Cover.SourcePath)
                            && string.IsNullOrEmpty(book.Cover.ThumbnailPath))
                        {
                            var thumb = _thumbnails.GetExistingPath(book.Cover.SourcePath!);
                            if (thumb != null)
                            {
                                book.Cover.ThumbnailPath = thumb;
                            }
                        }

                        foreach (var page in book.Pages.Where(p => p != null && !string.IsNullOrEmpty(p.SourcePath)
                                                                    && string.IsNullOrEmpty(p.ThumbnailPath)))
                        {
                            var thumb = _thumbnails.GetExistingPath(page.SourcePath!);
                            if (thumb != null)
                            {
                                page.ThumbnailPath = thumb;
                            }
                        }
                    }
                });
                PerfPhase.Mark("thumbnail paths applied");
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Не удалось обновить миниатюры слотов", ex);
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
            // There is nothing to ask about any more: every change has already gone to disk
            // through the autosave. This only makes sure the last gesture is written before
            // the screen it belongs to disappears.
            if (Books.Count > 0 || AvailableFiles.Count > 0)
            {
                if (!await QuickSaveAsync())
                {
                    // The project could not be written. Leaving would throw the work away,
                    // so the screen stays and the error message says why.
                    return;
                }
            }
            else
            {
                _autoSave.Cancel();
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
                    // The screen goes back either way - an empty project left in the list is
                    // the customer's to remove, and failing to remove it is not a reason to
                    // keep them on a screen with nothing in it.
                    _loggingService.LogError("Не удалось убрать пустой проект из списка", ex);
                }
            }

            RequestBackToProjectList();
        }
    }
}
