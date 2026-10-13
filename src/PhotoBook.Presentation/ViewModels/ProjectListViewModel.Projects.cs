using System.Windows.Input;
// System.Windows.Input is where ICommand lives in every engine, not only WPF -
// Avalonia kept the name for exactly this reason. Only System.Windows itself is
// the part that binds to one engine.
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoBook.Core;
using PhotoBook.Presentation;
using PhotoBook.Application;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>Showing the saved projects, and removing one.</summary>
    public partial class ProjectListViewModel : ViewModelBase
    {
        private async Task LoadProjectsAsync()
        {
            // Two loads can start at once: the constructor fires one and BuildView() asks
            // for another right after creating the ViewModel. Each of them only clears
            // Projects after awaiting the file reads, so they interleave as
            // clear / clear / add / add and every project shows up twice. A second load
            // while one is running reads the same data anyway, so skipping it is safe.
            if (_isLoading) return;

            IsLoading = true;
            ErrorMessage = null;

            try
            {

                var projects = await _projectListService.GetAllProjectsAsync();
                Projects.Clear();

                // A card is a copy. The list re-sorts and reloads while a project is
                // being opened, and the editor has to keep the record it was handed.
                foreach (var project in projects.OrderByDescending(p => p.LastModified))
                {
                    if (string.IsNullOrEmpty(project.Id))
                    {
                        continue;
                    }

                    // The stored counts go stale whenever a run is edited, so the file is
                    // opened and the real numbers read back. That is why showing the list
                    // touches every project file.
                    int actualPageCount = project.PageCount;
                    int actualBookCount = project.BookCount;
                    Project? previewSource = null;

                    if (!string.IsNullOrEmpty(project.FilePath) && File.Exists(project.FilePath))
                    {
                        try
                        {
                            var loadedProject = await _projects.LoadAsync(project.FilePath);
                            if (loadedProject != null && loadedProject.Books != null && loadedProject.Books.Count > 0)
                            {
                                actualPageCount = loadedProject.Books.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                                actualBookCount = loadedProject.Books.Count;

                                // The mockup's project card leads with a strip of three
                                // 48x48 photos plus a "+N" tile. The project is already
                                // fully loaded here, so the paths are simply read off it
                                // rather than re-opened per card.
                                previewSource = loadedProject;

                                if (actualPageCount != project.PageCount || actualBookCount != project.BookCount)
                                {
                                    project.PageCount = actualPageCount;
                                    project.BookCount = actualBookCount;
                                    Background.Run(
                                        async () => await _projectListService.SaveProjectInfoAsync(project),
                                        "save project index");
                                }
                            }
                        }
                        catch
                        {
                        }
                    }

                    var projectCopy = new ProjectInfo
                    {
                        Id = project.Id,
                        Name = project.Name,
                        FilePath = project.FilePath,
                        Mode = project.Mode,
                        BookCount = actualBookCount,
                        PageCount = actualPageCount,
                        Status = project.Status,
                        CreatedDate = project.CreatedDate,
                        LastModified = project.LastModified
                    };

                    // Preview strip: 3 photos + "+N". Filled before the card is added to
                    // Projects so the template's first layout pass already has the data.
                    if (previewSource != null)
                    {
                        FillPreviewPhotos(previewSource, projectCopy);
                    }

                    Projects.Add(projectCopy);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка загрузки проектов: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// How many photos the card shows as thumbnails. The mockup uses three, then
        /// puts the remainder in a "+N" tile.
        /// </summary>
        private const int PreviewPhotoLimit = 3;

        /// <summary>
        /// Fills a project card's preview strip from an already-loaded project.
        ///
        /// Covers come first, then spreads, book by book, because that is the order the
        /// photos appear in the finished book. Only paths that still exist on disk are
        /// taken: a project can outlive a moved or deleted photo, and a broken image in
        /// the strip would be worse than one tile fewer.
        /// </summary>
        private static void FillPreviewPhotos(Project project, ProjectInfo target)
        {
            if (project?.Books == null) return;

            int total = 0;

            foreach (var book in project.Books)
            {
                if (book == null) continue;

                // The cover is a separate object from Pages, and it is the first thing
                // the reader sees, so it leads the strip.
                if (book.Cover != null && !string.IsNullOrEmpty(book.Cover.SourcePath))
                {
                    total++;
                    if (target.PreviewPhotos.Count < PreviewPhotoLimit && File.Exists(book.Cover.SourcePath))
                        target.PreviewPhotos.Add(book.Cover.SourcePath);
                }

                if (book.Pages == null) continue;

                foreach (var page in book.Pages)
                {
                    if (page == null || string.IsNullOrEmpty(page.SourcePath)) continue;

                    total++;
                    if (target.PreviewPhotos.Count < PreviewPhotoLimit && File.Exists(page.SourcePath))
                        target.PreviewPhotos.Add(page.SourcePath);
                }
            }

            target.TotalPhotoCount = total;
        }

        private bool CanDeleteProject(ProjectInfo? projectInfo)
        {
            return projectInfo != null;
        }

        private async Task DeleteProjectAsync(ProjectInfo? projectInfo)
        {
            if (projectInfo == null) return;

            var result = await _dialogs.ConfirmAsync(
                "Удалить проект?",
                $"Проект \"{projectInfo.Name}\" будет удалён вместе со своей структурой.\n\n" +
                "Фотографии на диске останутся на месте — удаляется только проект.",
                "Удалить",
                destructive: true);

            if (result)
            {
                try
                {
                    var deleted = await _projectListService.DeleteProjectAsync(projectInfo);
                    if (deleted.IsSuccess)
                    {
                        Projects.Remove(projectInfo);
                    }
                    else
                    {
                        // What went wrong, not a fixed sentence that could have been anything.
                        ErrorMessage = deleted.Error;
                    }
                }
                catch (Exception ex)
                {
                    ErrorMessage = $"Ошибка удаления проекта: {ex.Message}";
                }
            }
        }
    }
}
