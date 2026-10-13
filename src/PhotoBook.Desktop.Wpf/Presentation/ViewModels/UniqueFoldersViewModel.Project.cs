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

namespace PhotoBookRenamer.Presentation.ViewModels
{
    /// <summary>Taking a project in, handing it back, and saying what state it is in.</summary>
    public partial class UniqueFoldersViewModel : ViewModelBase, IProjectEditor
    {

        public async void SetProject(Project? project, ProjectInfo projectInfo)
        {
            Books.Clear();
            Project = null;
            ProjectName = null;
            ErrorMessage = null;
            _editHistory.ClearHistory();
            Presentation.Converters.PageSourceConverter.ClearCache();

            // Everything about the project is read now, before the first await: the list
            // that handed this over may re-sort or reload its cards while this runs.
            var projectId = projectInfo.Id ?? string.Empty;
            var projectName = projectInfo.Name ?? string.Empty;
            var projectFilePath = projectInfo.FilePath ?? string.Empty;
            var projectMode = projectInfo.Mode;
            var projectBookCount = projectInfo.BookCount;
            var projectStatus = projectInfo.Status;
            var projectCreatedDate = projectInfo.CreatedDate;
            var projectLastModified = projectInfo.LastModified;

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
                BookCount = projectBookCount,
                Status = projectStatus,
                CreatedDate = projectCreatedDate,
                LastModified = projectLastModified
            };

            CurrentProjectInfo = projectInfoCopy;
            ProjectName = projectInfoCopy.Name;

            if (project == null)
            {
                project = new Project { Mode = projectInfo.Mode };
            }
            else
            {
                project.Mode = projectInfo.Mode;

                // A project written before spreads carried slots needs them recomputed,
                // because the slot frame is what assigns each spread its export file name.
                if (project.Books != null && project.Books.Count > 0)
                {
                    foreach (var book in project.Books)
                    {
                        // A project written before Pages was filled in comes back with them null.
                        if (book.Pages != null)
                        {
                            book.UpdatePageSlots();
                        }
                    }
                }
            }

            // Books and Project.Books must hold the same books in the same order: the
            // view binds to Books, the save walks Project.Books, and a mismatch loses
            // spreads silently.
            Books.Clear();
            if (project != null && project.Books != null)
            {
                foreach (var book in project.Books)
                {
                    Books.Add(book);
                }
            }

            Project = project;

            if (Project != null && Project.Books != null && Books.Count != Project.Books.Count)
            {
                Books.Clear();
                foreach (var book in Project.Books)
                {
                    Books.Add(book);
                }
            }

            if (Project != null && CurrentProjectInfo != null)
            {
                // PageCount is the spreads in ONE book, not the sum over all of them -
                // every book in a run has the same number.
                CurrentProjectInfo.PageCount = Project.Books?.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                CurrentProjectInfo.BookCount = Project.Books?.Count ?? 0;
                Background.Run(
                    async () => await _projectListService.SaveProjectInfoAsync(projectInfo),
                    "save project index");
            }

            OnPropertyChanged(nameof(Books));
            OnPropertyChanged(nameof(Project));
            OnPropertyChanged(nameof(ProjectName));

            if (SaveProjectCommand is AsyncRelayCommand saveCmd)
            {
                saveCmd.NotifyCanExecuteChanged();
            }

            // Previews that already exist are attached first: the path is computed by one
            // helper now, rather than by three copies of the same formula in two view models.
            if (Project != null && Project.Books != null && Project.Books.Count > 0)
            {
                foreach (var book in Project.Books)
                {
                    if (book.Cover != null)
                    {
                        book.Cover.ThumbnailPath = _thumbnails.GetExistingPath(book.Cover.SourcePath ?? string.Empty);
                    }

                    foreach (var page in book.Pages)
                    {
                        if (page != null)
                            page.ThumbnailPath = _thumbnails.GetExistingPath(page.SourcePath ?? string.Empty);
                    }
                }

                var allImagePaths = Project.Books
                    .SelectMany(b => b.Pages.Select(p => p.SourcePath).Concat(new[] { b.Cover?.SourcePath }))
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();

                // Preparation, in this order: the previews that are missing, then the
                // decoding away from the UI thread, then the slots. The reasoning is in
                // PrewarmAndWarmThumbnailsAsync (CombinedModeViewModel) - without it the
                // first render reads an 18 MB JPEG from inside the layout and the slots
                // stay empty grey frames until the project is saved and reopened.
                var preparation = BeginPreparation();
                Background.Run(async () =>
                {
                    await _thumbnails.EnsureAsync(allImagePaths!, cancellationToken: preparation);
                    await Presentation.Converters.PageSourceConverter.PrewarmAsync(allImagePaths!, preparation);

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var book in Project.Books)
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

                            foreach (var page in book.Pages.Where(p => !string.IsNullOrEmpty(p.SourcePath)))
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
                }, "prewarm and warm thumbnails");
            }
        }

        /// <summary>
        /// "Сохранить проект" in the project panel: writes the project and closes the
        /// session, the same as the header button.
        ///
        /// This replaces a 60-line copy of the save routine that refused to work on a
        /// project with no record yet ("Информация о проекте не найдена") - exactly the
        /// project a user has just created and wants to keep. QuickSaveAsync creates the
        /// record on demand and reports whether anything was written.
        /// </summary>
        private async Task SaveAndCloseAsync()
        {
            if (!await QuickSaveAsync())
            {
                ErrorMessage = "Не удалось сохранить проект.";
                OnPropertyChanged(nameof(ErrorMessage));
                return;
            }

            // The shell drops the project session on the way back - the mode tab goes
            // back out with it.
            RequestBackToProjectList();
        }

        /// <summary>
        /// Writes the name only: no project file, no change of screen.
        /// </summary>
        public async System.Threading.Tasks.Task SaveProjectNameOnlyAsync()
        {
            if (CurrentProjectInfo == null || string.IsNullOrEmpty(ProjectName))
            {
                return;
            }

            try
            {
                CurrentProjectInfo.Name = ProjectName;
                CurrentProjectInfo.LastModified = DateTime.Now;

            }
            catch (Exception ex)
            {
                _loggingService.LogError("Ошибка сохранения названия проекта", ex);
            }
        }

        private ProjectStatus DetermineProjectStatus(Project project)
        {
            if (project.Books.Count == 0)
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

        public ObservableCollection<Book> Books { get; }

        public Book? SelectedBook
        {
            get => _selectedBook;
            set => SetProperty(ref _selectedBook, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public Project? Project
        {
            get => _project;
            set
            {
                SetProperty(ref _project, value);
                if (value != null)
                {
                    Books.Clear();
                    foreach (var book in value.Books)
                    {
                        Books.Add(book);
                    }
                }
            }
        }

        /// <summary>
        /// Raised after a whole-project export has finished and the success dialog was
        /// dismissed - from the header "Экспорт" button or Ctrl+Shift+S. MainViewModel
        /// ends the project session on it and returns to the project list. Exporting a
        /// single book does NOT raise it: the project is still being worked on.
        /// </summary>
        public event EventHandler? ProjectExported;

    }
}
