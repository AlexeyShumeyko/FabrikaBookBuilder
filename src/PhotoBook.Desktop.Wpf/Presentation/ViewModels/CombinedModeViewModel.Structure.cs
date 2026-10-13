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
    /// <summary>Deciding how many books and spreads the run has, and holding to it.</summary>
    public partial class CombinedModeViewModel : ViewModelBase, IProjectEditor
    {

        private async Task LoadFilesAsync()
        {
            ErrorMessage = null;

            try
            {
                // IsLoading is raised AFTER the picker, not around it. The picker is its own
                // progress indicator, and the veil used to cover the whole window while it
                // was open - which is where we saw a grey background appear.
                var files = await _files.PickImagesAsync();

                if (files == null || files.Length == 0)
                    return;

                IsLoading = true;
                try
                {
                    // The first thing done with the files creates the project record: until now the
                    // tab could be open with no project behind it at all.
                    await EnsureProjectInfoAsync();

                    foreach (var file in files)
                    {
                        if (_fileService.IsJpegFile(file) && !AvailableFiles.Contains(file))
                        {
                            AvailableFiles.Add(file);
                        }
                    }

                    var preparation = BeginPreparation();
                    Background.Run(async () => await _thumbnails.EnsureAsync(AvailableFiles, cancellationToken: preparation), "load thumbnails");
                }
                finally
                {
                    IsLoading = false;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка: {ex.Message}";
                _loggingService.LogError("Ошибка загрузки файлов", ex);
            }
        }

        private void ClearFiles()
        {
            AvailableFiles.Clear();
        }

        private void GenerateStructure()
        {
            if (!IsStructureConfirmed)
            {
                return;
            }

            if (Books.Count == 0)
            {
                for (int i = 0; i < NumberOfBooks; i++)
                {
                    var book = new Book
                    {
                        BookIndex = i + 1,
                        Name = $"Книга {i + 1}",
                        Cover = new Page { IsCover = true, Index = 0 }
                    };

                    for (int j = 0; j < SpreadsPerBook; j++)
                    {
                        book.Pages.Add(new Page { IsCover = false, Index = j + 1, DisplayIndex = j + 1 });
                    }

                    book.UpdatePageSlots();
                    Books.Add(book);
                }

                OnPropertyChanged(nameof(Books));
            }
            else
            {
                var currentCount = Books.Count;

                // Growing the run is silent, shrinking it is not. Checked before anything
                // is touched, so cancelling leaves the structure exactly as it was.
                if (!ConfirmStructureChange(NumberOfBooks, SpreadsPerBook))
                    return;

                if (NumberOfBooks > currentCount)
                {
                    for (int i = currentCount; i < NumberOfBooks; i++)
                    {
                        var book = new Book
                        {
                            BookIndex = i + 1,
                            Name = $"Книга {i + 1}",
                            Cover = new Page { IsCover = true, Index = 0 }
                        };

                        // Straight from the requested count, not from the first book: every
                        // book of a run has the same number of spreads, and that is the
                        // whole point of the structure.
                        for (int j = 0; j < SpreadsPerBook; j++)
                        {
                            book.Pages.Add(new Page { IsCover = false, Index = j + 1, DisplayIndex = j + 1 });
                        }

                        book.UpdatePageSlots();
                        Books.Add(book);
                    }
                }
                else if (NumberOfBooks < currentCount)
                {
                    // From the end: the last books go, so the numbering of the ones that
                    // stay does not shift under the photographer.
                    while (Books.Count > NumberOfBooks)
                    {
                        Books.RemoveAt(Books.Count - 1);
                    }
                }

                SynchronizeSpreadsInAllBooks();

                for (int i = 0; i < Books.Count; i++)
                {
                    Books[i].BookIndex = i + 1;
                    Books[i].Name = $"Книга {i + 1}";
                }

                OnPropertyChanged(nameof(StructureProgress));
                OnPropertyChanged(nameof(StructureIsComplete));
            }

            if (Project == null)
            {
                Project = new Project
                {
                    Mode = AppMode.Combined
                };
            }

            Project.Books.Clear();
            foreach (var book in Books)
            {
                Project.Books.Add(book);
            }

            UpdateExportCommands();
        }

        /// <summary>
        /// Asks before the structure gets SMALLER; growing it needs no question.
        ///
        /// The message says how many photos are about to disappear, because the numbers in
        /// the two fields look harmless until the last spread of a book turns out to be
        /// filled. Removal always happens from the end: the last books, the last spreads.
        /// </summary>
        private bool ConfirmStructureChange(int targetBooks, int targetSpreads)
        {
            int booksLost = Books.Count - targetBooks;
            int spreadsLost = Books.Count == 0
                ? 0
                : Math.Max(0, Books.Max(b => b.Pages.Count(p => !p.IsCover)) - targetSpreads);

            if (booksLost <= 0 && spreadsLost <= 0) return true;

            int photosLost = 0;
            for (int i = 0; i < Books.Count; i++)
            {
                var book = Books[i];

                if (i >= targetBooks)
                {
                    // This whole book goes away, with everything already put in it.
                    photosLost += book.FilledSlotCount;
                    continue;
                }

                foreach (var page in book.Pages.Where(p => !p.IsCover && p.Index > targetSpreads))
                {
                    if (!page.IsEmpty) photosLost++;
                }
            }

            var parts = new List<string>();
            if (booksLost > 0)
                parts.Add($"книг: {booksLost}");

            if (spreadsLost > 0)
            {
                parts.Add($"разворотов в книге: {spreadsLost}");
                // Say the position that disappears, since that is what the user counts in.
                var firstLost = targetSpreads + 1;
                parts.Add($"последние развороты начиная с {firstLost}");
            }

            string photoLine = photosLost > 0
                ? $"\n\nВ них уже стоят фото — они будут удалены: {photosLost}."
                : "\n\nПустых слотов в удаляемом нет.";

            string message =
                $"Уменьшить структуру до {targetBooks} книг по {targetSpreads} разворотов?" +
                $"\n\nБудет удалено с конца — {string.Join("; ", parts)}.{photoLine}";

            return _dialogs.Confirm(
                "Уменьшить структуру?",
                message,
                "Уменьшить",
                destructive: true);
        }

        private void SynchronizeSpreadsInAllBooks()
        {
            if (Books.Count == 0) return;


            foreach (var book in Books)
            {
                var currentSpreads = book.Pages.Count(p => !p.IsCover);

                if (currentSpreads < SpreadsPerBook)
                {
                    for (int i = currentSpreads; i < SpreadsPerBook; i++)
                    {
                        var newIndex = i + 1;
                        book.Pages.Add(new Page { IsCover = false, Index = newIndex, DisplayIndex = newIndex });
                    }
                }
                else if (currentSpreads > SpreadsPerBook)
                {
                    var pagesToRemove = book.Pages.Where(p => !p.IsCover && p.Index > SpreadsPerBook).ToList();
                    foreach (var page in pagesToRemove)
                    {
                        if (string.IsNullOrEmpty(page.SourcePath))
                        {
                            book.Pages.Remove(page);
                        }
                    }

                    while (book.Pages.Count(p => !p.IsCover) > SpreadsPerBook)
                    {
                        var lastPage = book.Pages.Where(p => !p.IsCover).OrderByDescending(p => p.Index).FirstOrDefault();
                        if (lastPage != null)
                        {
                            book.Pages.Remove(lastPage);
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                var spreads = book.Pages.Where(p => !p.IsCover).OrderBy(p => p.Index).ToList();
                for (int i = 0; i < spreads.Count; i++)
                {
                    spreads[i].Index = i + 1;
                    spreads[i].DisplayIndex = i + 1;
                }

                book.UpdatePageSlots();
            }

        }

        private void ConfirmStructure()
        {
            IsStructureConfirmed = true;
            GenerateStructure();

            if (ConfirmStructureCommand is RelayCommand confirmCmd)
            {
                confirmCmd.NotifyCanExecuteChanged();
            }

            // One more automatic save, and only here. The structure is the most expensive
            // thing in a run to put back by hand, and setting it is a single deliberate act
            // rather than something done sixty times in a row - so it is written straight
            // away instead of waiting for a button we may never press. Photo
            // assignment is deliberately NOT auto-saved: that is where "без перебора" comes
            // in, and the export saves the whole project anyway.
            Background.Run(() => SaveStructureAsync(), "save structure");
        }

        private async Task SaveStructureAsync()
        {
            try
            {
                if (await EnsureProjectInfoAsync() == null) return;
                await SaveProjectSilentlyAsync();
            }
            catch (Exception ex)
            {
                // A failed auto-save must never interrupt the work: we can still
                // press "Сохранить проект", and the error log keeps the reason.
                _loggingService.LogError("Не удалось автоматически сохранить проект после изменения структуры", ex);
            }
        }

        /// <summary>
        /// Copies a measured size from one slot to another showing the same file. Cheaper
        /// and safer than re-reading the file, and it is how a run of shared spreads gets
        /// the right frame in every book at once.
        /// </summary>
        private static void CopyDimensions(PhotoBook.Core.Page from, PhotoBook.Core.Page to)
        {
            if (!from.HasDimensions || to.HasDimensions) return;
            if (!string.Equals(from.SourcePath, to.SourcePath, StringComparison.OrdinalIgnoreCase)) return;

            to.ImageWidth = from.ImageWidth;
            to.ImageHeight = from.ImageHeight;
        }

        /// <summary>
        /// Fills a slot's pixel size once a photo lands in it, then re-shapes the book.
        ///
        /// The frame ratio is the median of a book's own photo sizes, so a photo dropped
        /// into a slot without its size left the whole book on the default 16:10 - that is
        /// the "frames did not adapt" symptom. Fire-and-forget, exactly like the thumbnail
        /// load beside it: the photo appears at once and the frame follows a moment later.
        /// </summary>
        private async Task FillSlotDimensionsAsync(PhotoBook.Core.Page? page)
        {
            if (page == null || page.HasDimensions || string.IsNullOrEmpty(page.SourcePath)) return;

            try
            {
                var (width, height) = await _imageService.GetImageDimensionsAsync(page.SourcePath);
                if (width <= 0 || height <= 0) return;

                page.ImageWidth = width;
                page.ImageHeight = height;

                var book = Books.FirstOrDefault(b => b.Cover == page || b.Pages.Contains(page));
                book?.UpdatePageSlots();

                OnPropertyChanged(nameof(Books));
            }
            catch (Exception ex)
            {
                // The photo itself still shows; only the frame stays on the default ratio.
                _loggingService.LogError("Не удалось прочитать размер фото для слота", ex);
            }
        }

        public void DropFileOnSlot(PhotoBook.Core.Page page, string filePath, DropAction action, List<Book>? selectedBooks = null)
        {
            if (page == null || string.IsNullOrEmpty(filePath))
            {
                return;
            }

            if (action == DropAction.ThisBookOnly)
            {
                var book = Books.FirstOrDefault(b => b.Cover == page || b.Pages.Contains(page));
                var targetBook = Books.FirstOrDefault(b => b.Cover == page || b.Pages.Contains(page));

                if (!string.IsNullOrEmpty(page.SourcePath) && page.SourcePath != filePath)
                {
                    Presentation.Converters.PageSourceConverter.ClearCacheForFile(page.SourcePath);
                }

                Presentation.Converters.PageSourceConverter.ClearCacheForFile(filePath);
                page.SourcePath = filePath;

                if (targetBook != null)
                {
                    targetBook.UpdatePageSlots();
                }

                OnPropertyChanged(nameof(Books));
                LoadThumbnailForPage(page);
                Background.Run(() => FillSlotDimensionsAsync(page), "fill slot dimensions");
                UpdateExportCommands();
            }
            else if (action == DropAction.AllBooks)
            {
                foreach (var book in Books)
                {
                    PhotoBook.Core.Page? targetPage = null;
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
                        targetPage.SourcePath = filePath;
                        targetPage.IsLocked = true;
                        LoadThumbnailForPage(targetPage);
                        CopyDimensions(page, targetPage);
                    }
                }

                Background.Run(() => FillSlotDimensionsAsync(page), "fill slot dimensions");
                UpdateExportCommands();
            }
            else if (action == DropAction.SelectedBooks && selectedBooks != null)
            {
                foreach (var book in selectedBooks)
                {
                    PhotoBook.Core.Page? targetPage = null;
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
                        targetPage.SourcePath = filePath;
                        LoadThumbnailForPage(targetPage);
                        CopyDimensions(page, targetPage);
                    }
                }

                Background.Run(() => FillSlotDimensionsAsync(page), "fill slot dimensions");
                UpdateExportCommands();
            }
        }
    }
}
