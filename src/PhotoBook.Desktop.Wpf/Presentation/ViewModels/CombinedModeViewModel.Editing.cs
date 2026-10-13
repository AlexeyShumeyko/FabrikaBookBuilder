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
    /// <summary>Every change a photographer can make to what is inside a book.</summary>
    public partial class CombinedModeViewModel : ViewModelBase, IProjectEditor
    {

        private void DeleteBook(Book? book)
        {
            if (book == null) return;

            Books.Remove(book);

            for (int i = 0; i < Books.Count; i++)
            {
                Books[i].BookIndex = i + 1;
                Books[i].Name = $"Книга {i + 1}";
            }

            if (Project != null)
            {
                Project.Books.Clear();
                foreach (var b in Books)
                {
                    Project.Books.Add(b);
                }
            }

            UpdateExportCommands();
        }

        private void DuplicateBook(Book? book)
        {
            if (book == null) return;

            var newBook = new Book
            {
                BookIndex = Books.Count + 1,
                Name = $"Книга {Books.Count + 1}",
                Cover = new Page
                {
                    IsCover = true,
                    Index = 0,
                    SourcePath = book.Cover?.SourcePath,
                    ThumbnailPath = book.Cover?.ThumbnailPath,
                    // The copy shows the same file, so it carries the same size: a
                    // duplicated book keeps its frame instead of dropping to the default.
                    ImageWidth = book.Cover?.ImageWidth ?? 0,
                    ImageHeight = book.Cover?.ImageHeight ?? 0
                }
            };

            foreach (var page in book.Pages)
            {
                newBook.Pages.Add(new Page
                {
                    IsCover = false,
                    Index = page.Index,
                    DisplayIndex = page.DisplayIndex,
                    SourcePath = page.SourcePath,
                    ThumbnailPath = page.ThumbnailPath,
                    ImageWidth = page.ImageWidth,
                    ImageHeight = page.ImageHeight
                });
            }

            newBook.UpdatePageSlots();
            Books.Add(newBook);

            for (int i = 0; i < Books.Count; i++)
            {
                Books[i].BookIndex = i + 1;
                Books[i].Name = $"Книга {i + 1}";
            }

            NumberOfBooks = Books.Count;

            if (Project != null)
            {
                Project.Books.Clear();
                foreach (var b in Books)
                {
                    Project.Books.Add(b);
                }
            }

            UpdateExportCommands();
        }

        private void DeletePage(Page? page)
        {
            if (page == null) return;

            if (!string.IsNullOrEmpty(page.SourcePath))
            {
                _images.InvalidateFile(page.SourcePath);
            }

            page.SourcePath = null;
            page.ThumbnailPath = null;
            page.FileName = null;

            var book = Books.FirstOrDefault(b => b.Cover == page || b.Pages.Contains(page));
            if (book != null)
            {
                book.UpdatePageSlots();
            }

            OnPropertyChanged(nameof(Books));
            UpdateExportCommands();
        }

        private async void LoadPageFile(Page? page)
        {
            if (page == null) return;

            try
            {
                var files = await _files.PickImagesAsync();
                if (files == null || files.Length == 0) return;

                var file = files.FirstOrDefault(f => _fileService.IsJpegFile(f));
                if (file != null)
                {
                    if (!string.IsNullOrEmpty(page.SourcePath) && page.SourcePath != file)
                    {
                        _images.InvalidateFile(page.SourcePath);
                    }

                    _images.InvalidateFile(file);
                    page.SourcePath = file;

                    var book = Books.FirstOrDefault(b => b.Cover == page || b.Pages.Contains(page));
                    if (book != null)
                    {
                        book.UpdatePageSlots();
                    }

                    // Size first, then the frame: UpdatePageSlots is what re-shapes the
                    // book, so it has to run after the size is known.
                    await FillSlotDimensionsAsync(page);

                    await LoadThumbnailForPage(page);
                    OnPropertyChanged(nameof(Books));

                    if (!AvailableFiles.Contains(file))
                    {
                        AvailableFiles.Add(file);
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Ошибка загрузки файла для страницы", ex);
            }
        }

        private async void DuplicateToAllBooks(Page? page)
        {
            if (page == null || string.IsNullOrEmpty(page.SourcePath)) return;

            _images.InvalidateFile(page.SourcePath);

            var tasks = new List<Task>();
            foreach (var book in Books)
            {
                Page? targetPage = null;
                if (page.IsCover)
                {
                    targetPage = book.Cover;
                }
                else
                {
                    targetPage = book.Pages.FirstOrDefault(p => p.Index == page.Index);
                }

                if (targetPage != null)
                {
                    if (!string.IsNullOrEmpty(targetPage.SourcePath) && targetPage.SourcePath != page.SourcePath)
                    {
                        _images.InvalidateFile(targetPage.SourcePath);
                    }

                    targetPage.SourcePath = page.SourcePath;
                    CopyDimensions(page, targetPage);
                    book.UpdatePageSlots();
                    tasks.Add(LoadThumbnailForPage(targetPage));
                }
            }

            await Task.WhenAll(tasks);
            OnPropertyChanged(nameof(Books));
            UpdateExportCommands();
        }

        private void ResetProject()
        {
            Project = null;
            Books.Clear();
            AvailableFiles.Clear();
            ErrorMessage = null;
            IsStructureConfirmed = false;
            CurrentProjectInfo = null;
            ProjectName = null;
        }

        private void DeletePageFromAllBooks(Page? page)
        {
            if (page == null) return;

            var firstBook = Books.FirstOrDefault();
            if (firstBook == null) return;

            var pagesWithoutCover = firstBook.Pages.Where(p => !p.IsCover).ToList();
            var pageIndex = pagesWithoutCover.IndexOf(page);
            if (pageIndex < 0) return;

            foreach (var book in Books)
            {
                var pages = book.Pages.Where(p => !p.IsCover).ToList();
                if (pageIndex < pages.Count)
                {
                    var targetPage = pages[pageIndex];
                    targetPage.SourcePath = null;
                    targetPage.ThumbnailPath = null;
                    targetPage.FileName = null;
                }
            }

            UpdateExportCommands();
        }

        private void AddBook()
        {
            var spreadsCount = Books.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? SpreadsPerBook;

            var newBookIndex = Books.Count + 1;
            var newBook = new Book
            {
                BookIndex = newBookIndex,
                Name = $"Книга {newBookIndex}",
                Cover = new Page { IsCover = true, Index = 0 }
            };

            for (int j = 0; j < spreadsCount; j++)
            {
                newBook.Pages.Add(new Page { IsCover = false, Index = j + 1, DisplayIndex = j + 1 });
            }

            newBook.UpdatePageSlots();
            Books.Add(newBook);

            if (Project != null)
            {
                Project.Books.Clear();
                foreach (var b in Books)
                {
                    Project.Books.Add(b);
                }
            }

            UpdateExportCommands();
        }

        private void AddSpread()
        {
            var firstBook = Books.FirstOrDefault();
            if (firstBook == null) return;

            var newIndex = firstBook.Pages.Count(p => !p.IsCover) + 1;

            foreach (var book in Books)
            {
                book.Pages.Add(new Page { IsCover = false, Index = newIndex, DisplayIndex = newIndex });
                book.UpdatePageSlots();
            }

        }

        private void MovePageLeft(Page? page)
        {
            if (page == null || page.IsCover) return;

            var book = Books.FirstOrDefault(b => b.Cover == page || b.Pages.Contains(page));
            if (book == null) return;

            var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
            var pageIndex = pagesWithoutCover.IndexOf(page);
            if (pageIndex <= 0) return;

            var currentIndex = book.Pages.IndexOf(page);
            var targetIndex = currentIndex - 1;

            if (targetIndex >= 0 && !book.Pages[targetIndex].IsCover)
            {
                book.Pages.Move(currentIndex, targetIndex);
                UpdatePageDisplayIndices(book);
            }
        }

        private void MovePageRight(Page? page)
        {
            if (page == null || page.IsCover) return;

            var book = Books.FirstOrDefault(b => b.Cover == page || b.Pages.Contains(page));
            if (book == null) return;

            var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
            var pageIndex = pagesWithoutCover.IndexOf(page);
            if (pageIndex >= pagesWithoutCover.Count - 1) return;

            var currentIndex = book.Pages.IndexOf(page);
            var targetIndex = currentIndex + 1;

            if (targetIndex < book.Pages.Count)
            {
                book.Pages.Move(currentIndex, targetIndex);
                UpdatePageDisplayIndices(book);
            }
        }

        private void UpdatePageDisplayIndices(Book book)
        {
            var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
            for (int i = 0; i < pagesWithoutCover.Count; i++)
            {
                pagesWithoutCover[i].DisplayIndex = i + 1;
                pagesWithoutCover[i].Index = i + 1;
            }
        }

        private void Undo()
        {
        }

        private void Redo()
        {
        }
    }
}
