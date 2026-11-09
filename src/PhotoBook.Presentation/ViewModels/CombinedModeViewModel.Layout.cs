using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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
    /// <summary>Where every photograph sits, and whether that is allowed.</summary>
    public partial class CombinedModeViewModel : ViewModelBase, IProjectEditor
    {

        private void StampRunAspect(double aspect)
        {
            foreach (var book in Books)
                book.ApplyFrameAspect(aspect);
        }

        /// <summary>
        /// Nothing to forget: the run's shape is derived from the photos that are actually
        /// in the books right now, so an emptied project falls back to the default on its
        /// own. The latch that used to sit here is what left a rebuilt run in the old
        /// format.
        /// </summary>
        /// <summary>
        /// Works out what is "общий" - run-wide - and pushes it into both the slot labels
        /// and the file list. It is a fact about the run, not a flag anyone has to
        /// maintain: applying a photo to the same position of every book is all it takes.
        ///
        /// THE RULE, and the reason it is stated this precisely: a photo is run-wide at a
        /// position only if EVERY other book has that same photo AT THAT SAME POSITION.
        /// Counting "this file appears somewhere in two books" is not the same thing, and
        /// we reported the difference as a bug: a photograph pasted into spread 4 of
        /// one book, when another book happened to use it at spread 2, was labelled
        /// "Разворот 4 (общий)" and lost its "Во все книги" action. Sharing is what that
        /// button does; nothing else is.
        ///
        /// The same reasoning makes the file list's "Общий" mean the same thing: the file
        /// is the run-wide photo for at least one position. Assigned = it is in at least one
        /// book, free = in none. Five repeats inside one book stay "Назначен" - they are
        /// five spreads the photographer means to keep separate.
        /// </summary>
        private void RefreshPhotoUsage()
        {
            var bookCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Position -> the photo every book agrees on there. Key 0 is the cover.
            var runWide = new Dictionary<int, string>();

            foreach (var book in Books)
            {
                var inThisBook = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (book.Cover != null && !book.Cover.IsEmpty && !string.IsNullOrEmpty(book.Cover.SourcePath))
                    inThisBook.Add(book.Cover.SourcePath);

                foreach (var page in book.Pages)
                {
                    if (page == null || page.IsEmpty || string.IsNullOrEmpty(page.SourcePath)) continue;
                    inThisBook.Add(page.SourcePath);
                }

                foreach (var path in inThisBook)
                {
                    bookCounts.TryGetValue(path, out int n);
                    bookCounts[path] = n + 1;
                }
            }

            // A position qualifies only when every book - the first one included - holds
            // the same non-empty file there. A single empty slot makes the whole position
            // per-book, which is the honest answer: it is not spread across the run.
            foreach (var position in AllSlotPositions())
            {
                string? agreed = null;
                bool complete = true;

                foreach (var book in Books)
                {
                    var path = PathAt(book, position);
                    if (string.IsNullOrEmpty(path)) { complete = false; break; }

                    if (agreed == null) agreed = path;
                    else if (!string.Equals(agreed, path, StringComparison.OrdinalIgnoreCase))
                    {
                        complete = false;
                        break;
                    }
                }

                if (complete && agreed != null && Books.Count >= 2)
                    runWide[position] = agreed;
            }

            foreach (var file in PhotoFiles)
            {
                // "Общий" = the file is the run-wide photo somewhere, NOT merely that two
                // books use it somewhere. A photo used at different positions in different
                // books is the photographer's own repetition, and calling it shared would
                // hide the one button that makes it shared.
                bool shared = runWide.Values.Any(p => string.Equals(p, file.Path, StringComparison.OrdinalIgnoreCase));
                file.SetUsage(bookCounts.TryGetValue(file.Path, out int n) ? n : 0, shared);
            }

            foreach (var book in Books)
            {
                if (book.Cover != null)
                    book.Cover.IsShared = IsRunWide(book.Cover);

                foreach (var page in book.Pages)
                {
                    if (page != null) page.IsShared = IsRunWide(page);
                }
            }

            bool IsRunWide(PhotoBook.Core.Page page)
            {
                if (string.IsNullOrEmpty(page.SourcePath)) return false;
                return runWide.TryGetValue(page.Index, out string? path) &&
                       string.Equals(path, page.SourcePath, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>Every slot position of the run: 0 is the cover, then 1..N spreads.</summary>
        private IEnumerable<int> AllSlotPositions()
        {
            int max = 0;
            foreach (var book in Books)
            {
                if (book.Cover != null) max = Math.Max(max, 0);
                foreach (var page in book.Pages)
                    if (page != null && !page.IsCover) max = Math.Max(max, page.Index);
            }

            for (int i = 0; i <= max; i++) yield return i;
        }

        private static string? PathAt(Book book, int position)
            => position == 0
                ? (book.Cover is { IsEmpty: false } c ? c.SourcePath : null)
                : book.Pages.FirstOrDefault(p => p is { IsCover: false } && p.Index == position)?.SourcePath;

        public bool CanExport => Project?.IsValid ?? false;

        /// <summary>
        /// The book shown in the assembly canvas. The reference shows one book at a time
        /// with a chip strip to switch, instead of every book stacked vertically.
        /// Set by the ListBox's SelectedItem binding.
        /// </summary>
        public Book? SelectedBook
        {
            get => _selectedBook;
            set
            {
                if (SetProperty(ref _selectedBook, value))
                {
                    OnPropertyChanged(nameof(HasStructure));
                    OnPropertyChanged(nameof(SelectedBookProgress));
                }
            }
        }

        private Book? _selectedBook;

        public bool HasStructure => Books.Count > 0;

        public string SelectedBookProgress =>
            SelectedBook != null
                ? $"Готово {FilledSlots(SelectedBook)} из {TotalSlots(SelectedBook)}"
                : "Книг пока нет";

        /// <summary>"Готово 2 из 4" style readiness label for the first book.</summary>
        public string CurrentBookProgress =>
            Books.Count > 0 ? $"Готово {FilledSlots(Books[0])} из {TotalSlots(Books[0])}" : "Книг пока нет";

        private static int FilledSlots(Book b)
        {
            int n = 0;
            if (b.Cover != null && !b.Cover.IsEmpty) n++;
            foreach (var p in b.Pages)
                if (p != null && !p.IsCover && !p.IsEmpty) n++;
            return n;
        }

        private static int TotalSlots(Book b)
        {
            int n = b.Cover != null ? 1 : 0;
            foreach (var p in b.Pages)
                if (p != null && !p.IsCover) n++;
            return n;
        }

        public ProjectInfo? CurrentProjectInfo
        {
            get => _currentProjectInfo;
            private set
            {
                if (SetProperty(ref _currentProjectInfo, value))
                {
                    if (SaveProjectCommand is AsyncRelayCommand saveCmd)
                    {
                        saveCmd.NotifyCanExecuteChanged();
                    }
                }
            }
        }

        public string? ProjectName
        {
            get => _projectName;
            set
            {
                if (SetProperty(ref _projectName, value))
                {
                    // FallbackValue in XAML only fires when a path fails to resolve, not
                    // when the resolved value is null, so the placeholder lives here.
                    OnPropertyChanged(nameof(DisplayProjectName));
                }
            }
        }

        public string DisplayProjectName =>
            string.IsNullOrWhiteSpace(ProjectName) ? "Новый проект" : ProjectName!;

        /// <inheritdoc />
        public ProjectInfo? OpenProject => CurrentProjectInfo;

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
                await _projectListService.SaveProjectInfoAsync(CurrentProjectInfo);
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Ошибка сохранения названия проекта", ex);
            }
        }

        public bool IsStructureConfirmed
        {
            get => _isStructureConfirmed;
            set => SetProperty(ref _isStructureConfirmed, value);
        }

        private System.Threading.Timer? _generateTimer;

        /// <summary>Set while a field is handing its number over, so the box keeps the text.</summary>
        private bool _typingRunNumber;

        // What the two run fields say, character for character.
        //
        // Strings, and the numbers behind them stay numbers, on purpose. Bound straight to an
        // int, a field somebody has just emptied is a failed conversion - and the engine answers
        // that by printing the converter's own words under the box: a technical sentence in a
        // control panel, with the panel grown to fit it. The released program answered it with a
        // red frame. So the box keeps whatever was typed, the number keeps the last value that
        // made sense, the frame goes red while the text is not a number, and the structure
        // button waits rather than building a run out of a field nobody has filled in.
        private string _numberOfBooksText = "1";
        private string _spreadsPerBookText = "1";

        /// <summary>True while both run fields hold a number the run can be built from.</summary>
        public bool RunNumbersAreValid => NumberOfBooksIsValid && SpreadsPerBookIsValid;

        /// <summary>True while the books box holds a number. Drives its red frame.</summary>
        public bool NumberOfBooksIsValid => TryReadRunNumber(_numberOfBooksText, out _);

        /// <summary>True while the spreads box holds a number. Drives its red frame.</summary>
        public bool SpreadsPerBookIsValid => TryReadRunNumber(_spreadsPerBookText, out _);

        /// <summary>How many books the run is asked for, as typed.</summary>
        public string NumberOfBooksText
        {
            get => _numberOfBooksText;
            set
            {
                if (!SetProperty(ref _numberOfBooksText, value, nameof(NumberOfBooksText))) return;

                RaiseRunFieldState();

                if (TryReadRunNumber(value, out var books))
                {
                    AsTyping(() => NumberOfBooks = books);
                    return;
                }

                // No number in the box: nothing is confirmed and the button waits. The setter
                // of the number has already told the command about the good case.
                IsStructureConfirmed = false;
                NotifyStructureAvailability();
            }
        }

        /// <summary>How many spreads each book of the run is asked to have, as typed.</summary>
        public string SpreadsPerBookText
        {
            get => _spreadsPerBookText;
            set
            {
                if (!SetProperty(ref _spreadsPerBookText, value, nameof(SpreadsPerBookText))) return;

                RaiseRunFieldState();

                if (TryReadRunNumber(value, out var spreads))
                {
                    AsTyping(() => SpreadsPerBook = spreads);
                    return;
                }

                IsStructureConfirmed = false;
                NotifyStructureAvailability();
            }
        }

        public int NumberOfBooks
        {
            get => _numberOfBooks;
            set
            {
                if (value < 1) value = 1;
                if (value > 99) value = 99;

                if (SetProperty(ref _numberOfBooks, value))
                {
                    if (!_typingRunNumber) SetRunText(ref _numberOfBooksText, nameof(NumberOfBooksText), value);

                    IsStructureConfirmed = false;
                    NotifyStructureAvailability();
                }
            }
        }

        public int SpreadsPerBook
        {
            get => _spreadsPerBook;
            set
            {
                if (value < 1) value = 1;
                if (value > 99) value = 99;

                // Like NumberOfBooks, and deliberately NOT like it used to be: the setter
                // used to call SynchronizeSpreadsInAllBooks() the moment the field lost
                // focus, so typing a smaller number deleted the last spreads - and the
                // photos in them - before the button was ever pressed. The confirmation
                // then ran and correctly found nothing left to lose, so the question
                // never appeared and a filled spread was discarded silently.
                //
                // The number is now only a wish: the structure changes when the button is
                // pressed, and GenerateStructure asks first, exactly as it does for books.
                if (SetProperty(ref _spreadsPerBook, value))
                {
                    if (!_typingRunNumber) SetRunText(ref _spreadsPerBookText, nameof(SpreadsPerBookText), value);

                    IsStructureConfirmed = false;
                    NotifyStructureAvailability();
                }
            }
        }

        /// <summary>
        /// Writes a number the code chose into the box that shows it - a project being reopened,
        /// a book being duplicated. Never during typing: there the box owns its text.
        /// </summary>
        private void SetRunText(ref string field, string propertyName, int value)
        {
            if (!SetProperty(ref field, value.ToString(CultureInfo.InvariantCulture), propertyName)) return;
            RaiseRunFieldState();
        }

        private void RaiseRunFieldState()
        {
            OnPropertyChanged(nameof(NumberOfBooksIsValid));
            OnPropertyChanged(nameof(SpreadsPerBookIsValid));
            OnPropertyChanged(nameof(RunNumbersAreValid));
        }

        /// <summary>
        /// The number to build the run from, when the text is one. Anything else - empty, half
        /// typed, out of range - leaves the last good number alone and is answered by the red
        /// frame instead.
        /// </summary>
        private static bool TryReadRunNumber(string? text, out int number)
        {
            number = 0;

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return false;
            if (parsed < 1 || parsed > 99) return false;

            number = parsed;
            return true;
        }

        /// <summary>Tells the structure button to re-read whether it can be pressed.</summary>
        private void NotifyStructureAvailability()
        {
            if (ConfirmStructureCommand is RelayCommand confirmCmd)
            {
                confirmCmd.NotifyCanExecuteChanged();
            }
        }

        /// <summary>Assigns a number that came from the box, without sending the text back.</summary>
        private void AsTyping(Action assign)
        {
            _typingRunNumber = true;
            try
            {
                assign();
            }
            finally
            {
                _typingRunNumber = false;
            }
        }

        /// <summary>Raises the panel's run counter after a drop changed what is filled.</summary>
        private void RaiseStructureProgress()
        {
            OnPropertyChanged(nameof(StructureProgress));
            OnPropertyChanged(nameof(StructureIsComplete));
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
            set => SetProperty(ref _project, value);
        }

        public string? DraggedFile
        {
            get => _draggedFile;
            set => SetProperty(ref _draggedFile, value);
        }

        /// <summary>
        /// Raised after a whole-project export has finished and the success dialog was
        /// dismissed. MainViewModel ends the project session on it and returns to the
        /// project list.
        /// </summary>
        public event EventHandler? ProjectExported;
    }
}
