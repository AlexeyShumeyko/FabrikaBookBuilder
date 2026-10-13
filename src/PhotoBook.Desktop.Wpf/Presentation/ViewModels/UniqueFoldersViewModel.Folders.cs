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
    /// <summary>Choosing folders, validating them, and building the run from them.</summary>
    public partial class UniqueFoldersViewModel : ViewModelBase, IProjectEditor
    {



        private async Task LoadFoldersAsync()
        {
            ErrorMessage = null;

            try
            {
                var existingFolders = Project?.Books
                    .Where(b => !string.IsNullOrEmpty(b.FolderPath))
                    .Select(b => b.FolderPath!)
                    .ToList() ?? new List<string>();

                var dialog = new Presentation.Dialogs.MultiFolderBrowserDialog(_fileService, existingFolders);
                dialog.Owner = System.Windows.Application.Current.MainWindow;
                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var folders = dialog.SelectedFolders;
                if (folders.Count == 0)
                {
                    if (Project != null && existingFolders.Count > 0)
                    {
                        Project.Books.Clear();
                        Books.Clear();
                        _editHistory.SaveState(Project);
                    }
                    return;
                }

                var removedFolders = existingFolders.Except(folders).ToList();

                var newFolders = folders.Except(existingFolders).ToList();

                if (newFolders.Count == 0 && removedFolders.Count == 0 && existingFolders.Count > 0)
                {
                    return;
                }

                // The dialog already validated the folders; checked again because a file
                // can disappear between the two moments.
                var validationResult = await _fileService.ValidateFoldersDetailedAsync(folders);
                if (!validationResult.IsValid)
                {
                    ErrorMessage = validationResult.ErrorMessage ?? "Ошибка валидации папок.";
                    System.Windows.MessageBox.Show(
                        $"Не удалось загрузить папки:\n\n{validationResult.ErrorMessage}\n\nПожалуйста, исправьте ошибки и попробуйте снова.",
                        "Ошибка валидации",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    return;
                }

                IsLoading = true;

                // The first thing done with the folders creates the project record: until
                // now the tab could be open with no project behind it at all.
                await EnsureProjectInfoAsync();

                if (Project != null && existingFolders.Count > 0)
                {
                    if (removedFolders.Count > 0)
                    {
                        var booksToRemove = Project.Books
                            .Where(b => !string.IsNullOrEmpty(b.FolderPath) &&
                                       removedFolders.Contains(b.FolderPath))
                            .ToList();

                        foreach (var book in booksToRemove)
                        {
                            Project.Books.Remove(book);
                            Books.Remove(book);
                        }

                        _editHistory.SaveState(Project);
                    }

                    if (newFolders.Count > 0)
                    {
                        var newProject = await _projectBuilder.CreateProjectFromFoldersAsync(newFolders);
                        foreach (var book in newProject.Books)
                        {
                            Project.Books.Add(book);
                            Books.Add(book);
                        }

                        _editHistory.SaveState(Project);
                    }

                    for (int i = 0; i < Project.Books.Count; i++)
                    {
                        Project.Books[i].BookIndex = i + 1;
                    }
                }
                else
                {
                    Project = await _projectBuilder.CreateProjectFromFoldersAsync(folders);
                    _editHistory.SaveState(Project);

                    // Loading folders must not create a project record. A record belongs to
                    // "new project" or to the list, and inventing one here put an empty card into
                    // our list. The project has no record until the photographer saves
                    // it from the panel, which is the normal way to use it.
                }

                // Folders just added get the previews that already exist, attached right
                // away so the cards are filled when the list is drawn. The ones that are
                // missing are built by the background pass below: doing it here would
                // cost the UI thread a second per photograph.
                var booksToLoad = Project.Books.Where(b =>
                    newFolders.Contains(b.FolderPath ?? "")).ToList();

                foreach (var book in booksToLoad)
                {
                    if (book.Cover != null && !string.IsNullOrEmpty(book.Cover.SourcePath))
                    {
                        var thumbPath = ThumbnailStore.GetPath(book.Cover.SourcePath);

                        if (File.Exists(thumbPath))
                        {
                            book.Cover.ThumbnailPath = thumbPath;
                        }
                    }

                    foreach (var page in book.Pages.Where(p => !string.IsNullOrEmpty(p.SourcePath)))
                    {
                        var thumbPath = ThumbnailStore.GetPath(page.SourcePath);

                        if (File.Exists(thumbPath))
                        {
                            page.ThumbnailPath = thumbPath;
                        }
                    }
                }

                var allImagePaths = Project.Books
                    .SelectMany(b => b.Pages.Select(p => p.SourcePath).Concat(new[] { b.Cover?.SourcePath }))
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();

                var preparation = BeginPreparation();
                Background.Run(async () =>
                {
                    await _thumbnails.EnsureAsync(allImagePaths!, cancellationToken: preparation);
                    // The slots learn their preview paths here, on the UI thread, because
                    // that is the thread the binding reads from.
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var book in Project.Books)
                        {
                            if (book.Cover != null && !string.IsNullOrEmpty(book.Cover.SourcePath))
                            {
                                var thumbPath = ThumbnailStore.GetPath(book.Cover.SourcePath);

                                if (File.Exists(thumbPath) && book.Cover.ThumbnailPath != thumbPath)
                                {
                                    book.Cover.ThumbnailPath = thumbPath;
                                }
                            }

                            foreach (var page in book.Pages.Where(p => !string.IsNullOrEmpty(p.SourcePath)))
                            {
                                var thumbPath = ThumbnailStore.GetPath(page.SourcePath);

                                if (File.Exists(thumbPath) && page.ThumbnailPath != thumbPath)
                                {
                                    page.ThumbnailPath = thumbPath;
                                }
                            }
                        }
                    });
                }, "load thumbnails after folder scan");
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка: {ex.Message}";
                _loggingService.LogError("Ошибка загрузки папок", ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async void SelectCoverAsync(Book? book)
        {
            if (book == null) return;

            var files = await _fileService.GetJpegFilesAsync(book.FolderPath!);
            var selectedFiles = await _files.PickImagesAsync();

            if (selectedFiles != null && selectedFiles.Length > 0)
            {
                book.Cover = new Page
                {
                    SourcePath = selectedFiles[0],
                    IsCover = true,
                    Index = 0
                };
            }
        }
    }
}
