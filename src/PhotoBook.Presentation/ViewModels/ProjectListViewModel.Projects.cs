using System.Windows.Input;
// System.Windows.Input is where ICommand lives in every engine, not only WPF -
// Avalonia kept the name for exactly this reason. Only System.Windows itself is
// the part that binds to one engine.
using System;
using System.Collections.Generic;
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
                // The index, and nothing else.
                //
                // This used to open and parse every project file in the folder, one after
                // another, so that each card could be given its real book count, its real
                // spread count and three photograph paths. Thirty-nine cards meant reading
                // 1.2 MB on the way to this screen - every time the tab was pressed - and the
                // cost grew with the total size of everything on the machine rather than with
                // the number of cards.
                //
                // Those four things are now written into the index whenever a project is
                // saved, by the repository, which is the one place a project file is written
                // and therefore the one place that cannot forget. So the list reads what is
                // already known and draws.
                var projects = await _projectListService.GetAllProjectsAsync();

                Projects.Clear();

                foreach (var project in projects.OrderByDescending(p => p.LastModified))
                {
                    if (string.IsNullOrEmpty(project.Id)) continue;

                    // A card is a copy. The list re-sorts and reloads while a project is
                    // being opened, and the editor has to keep the record it was handed.
                    var projectCopy = new ProjectInfo
                    {
                        Id = project.Id,
                        Name = project.Name,
                        FilePath = project.FilePath,
                        Mode = project.Mode,
                        BookCount = project.BookCount,
                        PageCount = project.PageCount,
                        Status = project.Status,
                        CreatedDate = project.CreatedDate,
                        LastModified = project.LastModified
                    };

                    foreach (var photo in project.PreviewPhotos) projectCopy.PreviewPhotos.Add(photo);
                    projectCopy.TotalPhotoCount = project.TotalPhotoCount;

                    Projects.Add(projectCopy);
                }

                // The filters are applied once, over the finished list. Per project would
                // rebuild the visible set dozens of times for states nobody can see - the
                // intermediate results last microseconds, and the grid has not been laid
                // out yet.
                ApplyFilters();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка загрузки проектов: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }

            // Projects saved by a build before the index carried pictures still have none to
            // show. They are filled in here, after the list is already on screen and already
            // useful - once per project, ever, rather than on every visit.
            _ = BackfillPreviewsAsync();
        }

        /// <summary>
        /// Gives a picture strip to the projects that were saved before the index carried one.
        /// </summary>
        /// <remarks>
        /// Deliberately after <see cref="LoadProjectsAsync"/> has finished and never awaited by
        /// it: the whole point of the change was that this screen does not wait on the disk. A
        /// project with no strip shows plain tiles until this reaches it, and has one for good
        /// afterwards.
        ///
        /// Failures are swallowed one project at a time. A photograph that has been moved or
        /// deleted is not a reason to leave the rest of the list without pictures.
        /// </remarks>
        private async Task BackfillPreviewsAsync()
        {
            var needing = Projects.Where(p => p.PreviewPhotos.Count == 0 && !string.IsNullOrEmpty(p.FilePath))
                .Select(p => (Entry: p, Path: p.FilePath!))
                .ToList();

            if (needing.Count == 0) return;

            // Read on a worker thread, apply on the interface thread.
            //
            // The reading is the expensive part - thirty-nine files opened and parsed, and
            // hundreds of "is this photograph still there" checks - and doing it here would do
            // it ON the thread that is drawing the list, which is the thread the person is
            // looking at. This was measured: the list took about 170 ms longer than it needed
            // to, entirely while this ran underneath it.
            //
            // Applying has to come back. A card holds a bound collection, and changing it from
            // a worker thread raises the notification on the wrong thread, which is how a list
            // ends up with half its cards updated.
            var found = await Task.Run(async () =>
            {
                var results = new List<(ProjectInfo Entry, string[] Photos, int Total)>();

                foreach (var (entry, path) in needing)
                {
                    try
                    {
                        if (!File.Exists(path)) continue;

                        var loaded = await _projects.LoadAsync(path);
                        if (loaded is null) continue;

                        var copy = new ProjectInfo();
                        ProjectCardData.Fill(loaded, copy);
                        if (copy.PreviewPhotos.Count == 0) continue;

                        results.Add((entry, copy.PreviewPhotos.ToArray(), copy.TotalPhotoCount));
                    }
                    catch
                    {
                        // One unreadable project is one project with no strip.
                    }
                }

                return results;
            });

            foreach (var (entry, photos, total) in found)
            {
                try
                {
                    entry.PreviewPhotos.Clear();
                    foreach (var photo in photos) entry.PreviewPhotos.Add(photo);
                    entry.TotalPhotoCount = total;

                    await _projectListService.SaveProjectInfoAsync(entry);
                }
                catch
                {
                    // The list is already on screen and everything else is fine.
                }
            }
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
