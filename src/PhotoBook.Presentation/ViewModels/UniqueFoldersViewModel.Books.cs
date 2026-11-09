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

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>What changes what is inside a book: covers, spreads, order, undo.</summary>
    public partial class UniqueFoldersViewModel : ViewModelBase, IProjectEditor
    {

        private void DeleteBook(Book? book)
        {
            if (book == null || Project == null) return;

            _editHistory.SaveState(Project);

            Books.Remove(book);
            Project.Books.Remove(book);

            for (int i = 0; i < Project.Books.Count; i++)
            {
                Project.Books[i].BookIndex = i + 1;
            }
        }

        private void ResetProject()
        {
            // Starting over: whatever was being prepared for the previous project is not
            // wanted any more.
            StopPreparing();
            Project = null;
            Books.Clear();
            // The project record has to go too. This ViewModel is a singleton, so keeping
            // CurrentProjectInfo meant that "Создать проект" dropped the user into the
            // previous project - and the next save overwrote it. Combined mode already
            // cleared these two.
            CurrentProjectInfo = null;
            ProjectName = null;
            ErrorMessage = null;
            _editHistory.ClearHistory();
            _images.Invalidate(DecodedImageKind.Page);
        }

        private void Undo()
        {
            if (Project == null) return;
            var previousProject = _editHistory.Undo(Project);
            if (previousProject != null)
            {
                Project = previousProject;
            }
        }

        private void Redo()
        {
            if (Project == null) return;
            var nextProject = _editHistory.Redo(Project);
            if (nextProject != null)
            {
                Project = nextProject;
            }
        }

        private void MovePageUp(Page? page)
        {
            if (page == null || page.IsCover) return;

            var book = Books.FirstOrDefault(b => b.Pages.Contains(page));
            if (book == null) return;

            var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
            var pageIndex = pagesWithoutCover.IndexOf(page);
            if (pageIndex <= 0) return;

            _editHistory.SaveState(Project!);

            var currentIndex = book.Pages.IndexOf(page);
            var targetIndex = currentIndex - 1;

            if (targetIndex >= 0 && !book.Pages[targetIndex].IsCover)
            {
                book.Pages.Move(currentIndex, targetIndex);
                UpdatePageDisplayIndices(book);
            }
        }

        private void MovePageDown(Page? page)
        {
            if (page == null || page.IsCover) return;

            var book = Books.FirstOrDefault(b => b.Pages.Contains(page));
            if (book == null) return;

            var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
            var pageIndex = pagesWithoutCover.IndexOf(page);
            if (pageIndex >= pagesWithoutCover.Count - 1) return;

            _editHistory.SaveState(Project!);

            var currentIndex = book.Pages.IndexOf(page);
            var targetIndex = currentIndex + 1;

            if (targetIndex < book.Pages.Count)
            {
                book.Pages.Move(currentIndex, targetIndex);
                UpdatePageDisplayIndices(book);
            }
        }

        private async void AssignPageNumber(Page? page)
        {
            if (page == null || page.IsCover) return;

            var book = Books.FirstOrDefault(b => b.Pages.Contains(page));
            if (book == null) return;

            var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
            var maxPageNumber = pagesWithoutCover.Count;

            if (maxPageNumber == 0) return;

            var targetPageNumber = await _editorDialogs.AskPageNumber(maxPageNumber);
            if (targetPageNumber.HasValue)
            {
                var pageNumber = targetPageNumber.Value;

                _editHistory.SaveState(Project!);

                var currentIndex = book.Pages.IndexOf(page);

                // Page numbers start at 1 but the list carries the cover first, so the
                // position in the list is the page number.
                var targetIndexInCollection = pageNumber; // the cover sits at 0, so the spread number IS the position

                if (currentIndex == targetIndexInCollection)
                {
                    return;
                }

                book.Pages.RemoveAt(currentIndex);

                if (currentIndex < targetIndexInCollection)
                {
                    targetIndexInCollection--;
                }

                if (targetIndexInCollection <= book.Pages.Count)
                {
                    book.Pages.Insert(targetIndexInCollection, page);
                }
                else
                {
                    book.Pages.Add(page);
                }

                UpdatePageDisplayIndices(book);
            }
        }

        private void UpdatePageDisplayIndices(Book book)
        {
            var displayIndex = 1;
            foreach (var p in book.Pages.Where(p => !p.IsCover))
            {
                p.DisplayIndex = displayIndex;
                p.Index = displayIndex;
                displayIndex++;
            }

            // AllSlotsPages is built by ordering on Index, and the collection change that
            // got us here already rebuilt it using the OLD numbers. Without this second
            // pass the cards stay where they were and only their captions change, which
            // reads as "some other spread moved and mine did not".
            book.UpdatePageSlots();
        }

        /// <summary>
        /// Makes <paramref name="page"/> the cover of its book, keeping the file it already
        /// points at. The spread it used to be does not become a duplicate: the slot is
        /// simply given up, and the remaining spreads are renumbered.
        /// </summary>
        private void AssignCover(Page? page)
        {
            if (page == null || page.IsCover) return;
            if (Project == null) return;

            var book = Books.FirstOrDefault(b => b.Pages.Contains(page));
            if (book == null) return;

            var oldCover = book.Cover;

            _editHistory.SaveState(Project);

            // The promoted page leaves the spread list and becomes the cover.
            book.Pages.Remove(page);
            page.IsCover = true;
            page.Index = 0;
            page.DisplayIndex = 0;

            book.Cover = new Page
            {
                SourcePath = page.SourcePath,
                ThumbnailPath = page.ThumbnailPath,
                FileName = page.FileName,
                IsLocked = page.IsLocked,
                IsCover = true,
                Index = 0,
                DisplayIndex = 0
            };

            // The previous cover becomes a normal first spread instead of being lost.
            if (oldCover != null && !string.IsNullOrEmpty(oldCover.SourcePath))
            {
                oldCover.IsCover = false;
                book.Pages.Insert(0, oldCover);
            }

            UpdatePageDisplayIndices(book);
            book.UpdatePageSlots();
            OnPropertyChanged(nameof(Books));
            OnPropertyChanged(nameof(BooksSummary));
        }

        /// <summary>
        /// Restores spreads to the order they had when the folders were first read
        /// (alphabetical by file name). Does not touch covers or project metadata.
        ///
        /// Per book on purpose: the button lives in the book block, so pressing it must
        /// only re-sort that book. Re-ordering the whole project from one book's header
        /// is exactly the kind of surprise this used to cause.
        /// </summary>
        private void ResetBookOrder(Book? book)
        {
            if (Project == null || book == null) return;

            var current = book.Pages.Where(p => !p.IsCover).ToList();
            var sorted = current
                .OrderBy(p => p.SourcePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (current.SequenceEqual(sorted)) return;

            _editHistory.SaveState(Project);

            foreach (var p in current) book.Pages.Remove(p);
            foreach (var p in sorted) book.Pages.Add(p);

            UpdatePageDisplayIndices(book);
        }
    }
}
