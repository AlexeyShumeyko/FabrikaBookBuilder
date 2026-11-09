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

        private async Task GenerateStructure()
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
                if (!await ConfirmStructureChange(NumberOfBooks, SpreadsPerBook))
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
        private async Task<bool> ConfirmStructureChange(int targetBooks, int targetSpreads)
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

            return await _dialogs.ConfirmAsync(
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

        private async void ConfirmStructure()
        {
            IsStructureConfirmed = true;

            // Awaited, and this is the whole of why: a structure that is getting smaller
            // stops here and asks a question. Starting the work and then immediately asking
            // someone else to save it meant the save ran while the question was still open -
            // so it wrote the structure the person was in the middle of changing, and when
            // they answered, the change they had confirmed was never written at all.
            //
            // Which one happened first was never decided by the code, only by how quickly
            // each side got going, and in the shell being built the save won. A structure
            // that shrinks and then does not stay shrunk is not a question of speed.
            await GenerateStructure();

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
            await SaveStructureAsync();
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
        /// Starts the measurement of a slot's photograph without waiting for it.
        /// </summary>
        private void FillSlotDimensionsSoon(PhotoBook.Core.Page page)
            => Background.Run(() => FillSlotDimensionsAsync(page), "fill slot dimensions");

        /// <summary>
        /// Fills a slot's pixel size once a photo lands in it, then re-shapes the book.
        ///
        /// The frame ratio is the median of a book's own photo sizes, so a photo dropped
        /// into a slot without its size left the whole book on the default 16:10 - that is
        /// the "frames did not adapt" symptom. Fire-and-forget, exactly like the thumbnail
        /// load beside it: the photo appears at once and the frame follows a moment later.
        /// </summary>
        /// <remarks>
        /// What happens after the await runs on the drawing thread, and that is the fix for a
        /// visible defect rather than tidiness. The measurement resumes on the thread pool, and
        /// the two lines below write into a page and then rebuild the book's slot collections -
        /// collections an ItemsControl is bound to. Clearing one of those from a pool thread made
        /// the list duplicate its own item containers: dropping a photo into a book with two
        /// spreads painted a dozen filled frames into that one book, while the project on disk
        /// was correct all along and reopening it showed the single photo in the right slot.
        /// </remarks>
        private async Task FillSlotDimensionsAsync(PhotoBook.Core.Page? page)
        {
            if (page == null || page.HasDimensions || string.IsNullOrEmpty(page.SourcePath)) return;

            var path = page.SourcePath;

            try
            {
                var (width, height) = await _imageService.GetImageDimensionsAsync(path);
                if (width <= 0 || height <= 0) return;

                RunOnUi(() => ApplySlotDimensions(page, path, width, height));
            }
            catch (Exception ex)
            {
                // The photo itself still shows; only the frame stays on the default ratio.
                _loggingService.LogError("Не удалось прочитать размер фото для слота", ex);
            }
        }

        /// <summary>The write-back half of <see cref="FillSlotDimensionsAsync"/>.</summary>
        private void ApplySlotDimensions(PhotoBook.Core.Page page, string path, int width, int height)
        {
            // The slot may have been emptied or given another photo while the file was being
            // measured; a size that belongs to neither is worse than a frame on the default.
            if (!string.Equals(page.SourcePath, path, StringComparison.OrdinalIgnoreCase)) return;

            page.ImageWidth = width;
            page.ImageHeight = height;

            // An all-books drop puts one file into the same slot of every book, and each of
            // those slots has forgotten its size when its source changed. The header has
            // been read once already, so the answer is copied rather than read again - and
            // it only goes to slots that really show this file, never to a neighbour.
            foreach (var book in Books)
            {
                if (book.Cover != null) CopyDimensions(page, book.Cover);
                foreach (var candidate in book.Pages)
                    CopyDimensions(page, candidate);
            }

            // The frame follows the size. Every book is stamped from the run's majority, so
            // the first measured spread re-shapes the whole project at once.
            UpdateRunFrameAspect();

            OnPropertyChanged(nameof(Books));
        }

        /// <summary>
        /// Puts a dropped photograph into one slot, or into the same slot of several books.
        /// </summary>
        /// <remarks>
        /// The slot is not rebuilt: it already exists, and clearing the collections the cards
        /// are bound to is what used to leave stale frames behind. What changes is the file
        /// the slot points at, its reduced copy and its measured size. Assigning
        /// <see cref="PhotoBook.Core.Page.SourcePath"/> forgets the previous file's size and
        /// thumbnail, so a filled slot takes a replacement exactly like an empty one does.
        ///
        /// The header is read for the first target only; <see cref="ApplySlotDimensions"/>
        /// copies that answer to every slot showing the same file.
        /// </remarks>
        public void DropFileOnSlot(PhotoBook.Core.Page page, string filePath, DropAction action, List<Book>? selectedBooks = null)
        {
            if (page == null || string.IsNullOrEmpty(filePath))
            {
                return;
            }

            _images.InvalidateFile(filePath);

            var targets = new List<PhotoBook.Core.Page>();
            if (action == DropAction.ThisBookOnly)
            {
                targets.Add(page);
            }
            else
            {
                IEnumerable<Book>? books = action == DropAction.AllBooks ? Books : selectedBooks;
                if (books != null)
                {
                    foreach (var book in books)
                    {
                        var targetPage = page.IsCover
                            ? book.Cover
                            : book.Pages.FirstOrDefault(p => p.Index == page.Index);

                        if (targetPage != null) targets.Add(targetPage);
                    }
                }
            }

            if (targets.Count == 0) return;

            foreach (var targetPage in targets)
            {
                if (!string.IsNullOrEmpty(targetPage.SourcePath)
                    && !string.Equals(targetPage.SourcePath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    _images.InvalidateFile(targetPage.SourcePath);
                }

                targetPage.SourcePath = filePath;
                if (action == DropAction.AllBooks) targetPage.IsLocked = true;

                var targetBook = Books.FirstOrDefault(b => b.Cover == targetPage || b.Pages.Contains(targetPage));
                targetBook?.RefreshSlots();

                _ = LoadThumbnailForPage(targetPage);
            }

            FillSlotDimensionsSoon(targets[0]);

            UpdateRunFrameAspect();
            OnPropertyChanged(nameof(Books));
            UpdateExportCommands();
            RaiseStructureProgress();
        }
    }
}
