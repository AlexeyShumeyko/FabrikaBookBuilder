using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>The order page's own head: what the run is worth, and what holds it back.</summary>
    /// <remarks>
    /// <para>
    /// Three kinds of number, and they answer the three questions a customer has before pressing
    /// the button: what am I sending, what is it worth, and what is wrong. The page used to
    /// answer only the second, in a panel, with the first and third left to the group cards -
    /// which made a page of four groups read as four unrelated projects.
    /// </para>
    /// <para>
    /// The counts are of what goes: a group the customer took out of the run is not part of "к
    /// отправке", and saying it was would make the price and the books disagree. How many
    /// groups stand aside is said beside it, once, because that is a fact about the run rather
    /// than a warning about a group.
    /// </para>
    /// </remarks>
    public sealed partial class SiteOrderViewModel
    {
        /// <summary>Every book the page holds, whether it goes or not.</summary>
        private IEnumerable<OrderBookRow> AllBooks => Groups.SelectMany(group => group.Books);

        /// <summary>Every book that goes in the coming run.</summary>
        private IEnumerable<OrderBookRow> GoingBooks =>
            Groups.Where(group => !group.IsExcluded).SelectMany(group => group.IncludedBooks);

        /// <summary>Groups that go in the coming run.</summary>
        private IEnumerable<SiteOrderGroup> GoingGroups => Groups.Where(group => !group.IsExcluded);

        /// <summary>Books the run sends.</summary>
        public int GoingBookCount => GoingBooks.Count();

        /// <summary>Files the run sends, one per page that holds a photograph.</summary>
        public int GoingFileCount => GoingGroups.Sum(group => group.FileCount);

        /// <summary>The word the strip prints after the count: «2 книги» — this is «книги».</summary>
        /// <remarks>
        /// The count and its word are two runs, not one sentence: the prototype sets the number
        /// at 15 extra bold and the word beside it at 11.5 in the muted ink, and a sentence
        /// would print both the same size.
        /// </remarks>
        public string GoingBooksUnit =>
            RussianPlural.Of(GoingBookCount, "книга", "книги", "книг");

        /// <summary>The word after the file count: «10 файлов» — this is «файлов».</summary>
        public string GoingFilesUnit =>
            RussianPlural.Of(GoingFileCount, "файл", "файла", "файлов");

        /// <summary>The project's name in the sentence under the page's name: «выпуск».</summary>
        /// <remarks>
        /// Quoted and bound on its own because it is the one bold run of that sentence: the
        /// prototype writes «Проект **«выпуск»** · 4 группы · 13 книг · 65 файлов», and the
        /// name is what the eye is meant to catch there.
        /// </remarks>
        public string ProjectNameText => "«" + ProjectName + "»";

        /// <summary>The rest of that sentence: «· 4 группы · 13 книг · 65 файлов».</summary>
        /// <remarks>
        /// All of the project, not only what goes: the sentence introduces the page, and a page
        /// that opened by saying it held two books out of thirteen would read as a page about
        /// two books. What goes is in the strip directly under it.
        /// </remarks>
        public string HeaderSummary => Groups.Count == 0
            ? string.Empty
            : " · " +
              RussianPlural.With(Groups.Count, "группа", "группы", "групп") + " · " +
              RussianPlural.With(AllBooks.Count(), "книга", "книги", "книг") + " · " +
              RussianPlural.With(
                  Groups.Sum(group => group.Books.Sum(book => book.Book.Pages.Count(page => !page.IsEmpty))),
                  "файл", "файла", "файлов");

        /// <summary>Groups that go and still cannot.</summary>
        public int AttentionCount => GoingGroups.Count(group => group.HasProblem);

        /// <summary>Whether anything needs an answer before the run.</summary>
        public bool HasAttentionTag => AttentionCount > 0;

        /// <summary>«1 группа требует внимания», «2 группы требуют внимания».</summary>
        public string AttentionTagText =>
            RussianPlural.With(AttentionCount, "группа требует", "группы требуют", "групп требуют") +
            " внимания";

        /// <summary>Groups the customer took out of the run.</summary>
        public int ExcludedCount => Groups.Count(group => group.IsExcluded);

        /// <summary>Whether any group stands aside.</summary>
        public bool HasExcludedTag => ExcludedCount > 0;

        /// <summary>«1 группа не участвует», «2 группы не участвуют».</summary>
        public string ExcludedTagText =>
            RussianPlural.With(ExcludedCount, "группа не участвует", "группы не участвуют", "групп не участвуют");

        /// <summary>Books that go and are not ready, which is what the warm plate is about.</summary>
        public int ProblemBookCount => GoingBooks.Count(book => !book.IsReady || book.IsFailed);

        /// <summary>Whether the warm plate has anything to say.</summary>
        public bool HasProblemAlert => ProblemBookCount > 0 || AttentionCount > 0;

        /// <summary>«1 книга не готова к загрузке», or the same about groups.</summary>
        public string AlertTitle
        {
            get
            {
                if (ProblemBookCount > 0)
                    return RussianPlural.With(ProblemBookCount, "книга не готова", "книги не готовы", "книг не готовы") +
                        " к загрузке";

                if (AttentionCount > 0)
                    return RussianPlural.With(AttentionCount, "группа не готова", "группы не готовы", "групп не готовы") +
                        " к загрузке";

                return string.Empty;
            }
        }

        /// <summary>Which books, by name, and what that means for the rest.</summary>
        /// <remarks>
        /// Three names and a count rather than every name: the plate is a signpost, and its
        /// button goes to the group. A list of thirteen books would be the page under it again,
        /// in smaller type.
        /// </remarks>
        public string AlertText
        {
            get
            {
                var names = GoingBooks
                    .Where(book => !book.IsReady || book.IsFailed)
                    .Select(book => "«" + book.Name + "»")
                    .ToList();

                if (names.Count > 0)
                {
                    var shown = string.Join(", ", names.Take(3));
                    if (names.Count > 3)
                        shown += " и ещё " + RussianPlural.With(names.Count - 3, "книга", "книги", "книг");

                    return "Не прошли проверку: " + shown + ". Остальные книги отправятся как обычно.";
                }

                return FirstProblemGroup?.ProblemText.Trim() ?? string.Empty;
            }
        }

        /// <summary>The group the plate's button goes to: the first one that needs an answer.</summary>
        public SiteOrderGroup? FirstProblemGroup =>
            Groups.FirstOrDefault(group => !group.IsExcluded && group.HasProblem);

        /// <summary>Tells the page's head everything that follows from the order changing.</summary>
        private void NotifyCard()
        {
            OnPropertyChanged(nameof(GoingBookCount));
            OnPropertyChanged(nameof(GoingFileCount));
            OnPropertyChanged(nameof(GoingBooksUnit));
            OnPropertyChanged(nameof(GoingFilesUnit));
            OnPropertyChanged(nameof(ProjectNameText));
            OnPropertyChanged(nameof(HeaderSummary));
            OnPropertyChanged(nameof(AttentionCount));
            OnPropertyChanged(nameof(HasAttentionTag));
            OnPropertyChanged(nameof(AttentionTagText));
            OnPropertyChanged(nameof(ExcludedCount));
            OnPropertyChanged(nameof(HasExcludedTag));
            OnPropertyChanged(nameof(ExcludedTagText));
            OnPropertyChanged(nameof(ProblemBookCount));
            OnPropertyChanged(nameof(HasProblemAlert));
            OnPropertyChanged(nameof(AlertTitle));
            OnPropertyChanged(nameof(AlertText));
            OnPropertyChanged(nameof(FirstProblemGroup));
        }
    }
}
