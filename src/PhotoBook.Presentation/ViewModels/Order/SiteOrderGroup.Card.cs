using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>What one group's card shows above its books: its weight and its state.</summary>
    /// <remarks>
    /// <para>
    /// The card is read from the books that go, not from all of them: a book the customer took
    /// out of the run is neither counted nor priced, so a group says what it will send rather
    /// than what it holds.
    /// </para>
    /// <para>
    /// The state is three words instead of a sentence, because the sentence is already written
    /// under them: a badge that repeated the reason would be the same text twice. A group that
    /// is fine says so quietly in green; one that needs an answer says so in the warm tone; one
    /// that was taken out or never set up says so in the quiet grey.
    /// </para>
    /// </remarks>
    public sealed partial class SiteOrderGroup
    {
        private bool _collapsed;

        /// <summary>Whether the card's books are folded away, leaving its head in place.</summary>
        /// <remarks>
        /// Purely the card's own state, and it survives nothing: the page is read from the
        /// project on every visit, and a group folded away on one visit is not a choice about
        /// the order. Four groups of thirteen books are a long page, and folding the ones that
        /// are finished is how the eye gets to the one that is not.
        /// </remarks>
        public bool IsCollapsed
        {
            get => _collapsed;
            set
            {
                if (!SetProperty(ref _collapsed, value))
                    return;

                OnPropertyChanged(nameof(IsExpanded));
            }
        }

        /// <summary>Whether the card's books are on screen.</summary>
        public bool IsExpanded => !_collapsed;

        /// <summary>Folds the card away, or unfolds it.</summary>
        public void ToggleCollapse() => IsCollapsed = !IsCollapsed;

        /// <summary>How the card is faded when the group is out of the run.</summary>
        public double CardOpacity => IsExcluded ? 0.55 : 1.0;

        /// <summary>Books of this group that go in the coming run.</summary>
        public int IncludedBookCount => IncludedBooks.Count;

        /// <summary>Spreads those books have, the covers not counted.</summary>
        public int SpreadCount => IncludedBooks.Sum(book => book.Spreads);

        /// <summary>
        /// Files the group would send: one per page that holds a photograph.
        /// </summary>
        /// <remarks>
        /// The same counting the export writes and the site receives - a cover and one file per
        /// spread - so the number on the card is the number of files the run will put up rather
        /// than a number of photographs that happens to be nearby.
        /// </remarks>
        public int FileCount => IncludedBooks.Sum(book => book.Book.Pages.Count(page => !page.IsEmpty));

        /// <summary>The group in one line: how much of it there is.</summary>
        public string CompositionText =>
            RussianPlural.With(IncludedBookCount, "книга", "книги", "книг") + " · " +
            RussianPlural.With(SpreadCount, "разворот", "разворота", "разворотов") + " · " +
            RussianPlural.With(FileCount, "файл", "файла", "файлов");

        /// <summary>What the card says instead of a price until the site has named one.</summary>
        public string SumText => HasTotal ? TotalText : "Параметры не выбраны";

        /// <summary>Whether the sum is a price rather than that note.</summary>
        public bool HasSum => HasTotal;

        /// <summary>Books of the group that are ready to go.</summary>
        /// <remarks>
        /// The count is of books the order would actually send: a full book whose files the site
        /// refused is not ready, and a panel that said «2 из 2 книг готовы» over two errors was
        /// the one place on the page still claiming the group was fine.
        /// </remarks>
        public int ReadyBookCount =>
            IncludedBooks.Count(book => book.IsReady && book.SelectedFormat is not null && !book.IsFailed);

        /// <summary>How many of the group's books are answered, as the panel's foot says it.</summary>
        public string ReadyBooksText
        {
            get
            {
                if (IncludedBookCount == 1)
                    return ReadyBookCount == 1 ? "Книга готова" : "Книга не готова";

                return ReadyBookCount + " из " + IncludedBookCount + " книг " +
                    (ReadyBookCount == 1 ? "готова" : "готовы");
            }
        }

        /// <summary>The group's state in one word, for the badge on its head.</summary>
        public string StatusTagText
        {
            get
            {
                if (IsExcluded)
                    return "Не участвует";

                if (IsBusy)
                    return "Проверяем…";

                if (CanGo)
                    return "Готова";

                // Nothing to check against yet: the format is what the site prices by, so a
                // group without one is not broken - it is unconfigured.
                return SelectedFormat is null ? "Не настроена" : "Требует внимания";
            }
        }

        /// <summary>Whether the badge is the green one.</summary>
        public bool StatusIsReady => !IsExcluded && !IsBusy && CanGo;

        /// <summary>Whether the badge is the warm one: something needs an answer.</summary>
        public bool StatusIsProblem => !IsExcluded && !IsBusy && !CanGo && SelectedFormat is not null;

        /// <summary>Whether the badge is the quiet one.</summary>
        public bool StatusIsMuted => IsExcluded || IsBusy || SelectedFormat is null;

        /// <summary>Whether the group is going and still cannot go.</summary>
        public bool HasProblem => !IsExcluded && !CanGo;

        /// <summary>Tells the card everything that follows from the group changing.</summary>
        private void NotifyCard()
        {
            OnPropertyChanged(nameof(IncludedBookCount));
            OnPropertyChanged(nameof(SpreadCount));
            OnPropertyChanged(nameof(FileCount));
            OnPropertyChanged(nameof(CompositionText));
            OnPropertyChanged(nameof(SumText));
            OnPropertyChanged(nameof(HasSum));
            OnPropertyChanged(nameof(ReadyBookCount));
            OnPropertyChanged(nameof(ReadyBooksText));
            OnPropertyChanged(nameof(StatusTagText));
            OnPropertyChanged(nameof(StatusIsReady));
            OnPropertyChanged(nameof(StatusIsProblem));
            OnPropertyChanged(nameof(StatusIsMuted));
            OnPropertyChanged(nameof(HasProblem));
            OnPropertyChanged(nameof(CardOpacity));
        }
    }
}
