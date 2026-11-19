using System;
using System.Collections.Generic;
using System.Linq;
using CoreBook = PhotoBook.Core.Book;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>What one group says about itself on its own card.</summary>
    /// <remarks>
    /// <para>
    /// Every sentence here is about this group alone - its books, its format, its answers, its
    /// files - so it is written on the group's card and nowhere else. The page used to collect
    /// the same sentences in a card over the groups, which repeated the numbers and left the
    /// customer to find the group; a book's own complaint now lives in the book's row instead.
    /// </para>
    /// <para>
    /// The wording names the next action, not the program's state: which parameters are still
    /// unchosen, which book is not filled, that the site will not sell this combination. A
    /// customer should never have to guess what "the order is not assembled" means.
    /// </para>
    /// </remarks>
    public sealed partial class SiteOrderGroup
    {
        /// <summary>Whether the coming run leaves this group out while other groups go.</summary>
        private bool _staysBehind;

        /// <summary>
        /// The group's own line: why it cannot go, and whether its books stay behind.
        /// </summary>
        public string NoteText
        {
            get
            {
                if (IsExcluded)
                    return HasUploadedProject
                        ? "Группа не будет загружена снова — она уже на сайте, проект " + UploadedProjectId +
                          ". Чтобы отправить её ещё раз, отметьте «Участвует в загрузке»."
                        : "Группа исключена из загрузки — её книги не будут отправлены.";

                var problem = ProblemText.Trim();
                if (problem.Length == 0)
                    return string.Empty;

                if (!_staysBehind)
                    return problem;

                // While the group is still reading, the honest sentence is "wait", not "these
                // books are left out": the customer has not done anything wrong yet.
                return !_parametersLoaded || IsBusy
                    ? problem + " Дождитесь окончания — иначе книги этой группы не попадут в загрузку."
                    : problem + " Книги этой группы загружены не будут.";
            }
        }

        /// <summary>Whether the group has anything to say on its card.</summary>
        public bool HasNote => NoteText.Length > 0;

        /// <summary>Whether the line is a warning about books left behind rather than a reason.</summary>
        public bool NoteIsWarning => _staysBehind && HasNote;

        /// <summary>Whether the line is the group's own reason for not being ready.</summary>
        public bool NoteIsProblem => HasNote && !_staysBehind;

        /// <summary>
        /// Says whether this group is one the coming run leaves out, so its card reads as a
        /// warning rather than as a reason beside a button.
        /// </summary>
        public void MarkStaying(bool stays)
        {
            if (_staysBehind == stays)
                return;

            _staysBehind = stays;
            NotifyProblem();
        }

        /// <summary>
        /// The group is ready to go, in one green word rather than a line of grey text.
        /// </summary>
        /// <remarks>
        /// A ready group has nothing to explain, so the old "files: 2 books, everything matches"
        /// line was a sentence nobody reads. What the customer needs is the opposite case - and
        /// that case is <see cref="NoteText"/>.
        /// </remarks>
        public string StatusText => CanGo ? "Готова к загрузке" : string.Empty;

        /// <summary>Whether the ready badge is shown.</summary>
        public bool HasStatus => CanGo;

        /// <summary>Why the group cannot go, in the customer's words; empty when it can.</summary>
        public string ProblemText
        {
            get
            {
                var included = IncludedBooks;

                if (included.Count == 0)
                    return Books.Count == 0
                        ? "В группе нет ни одной книги."
                        : "Все книги группы исключены из загрузки.";

                var incomplete = included
                    .Where(book => !book.IsReady)
                    .Select(book => "«" + book.Name + "»")
                    .ToList();

                if (incomplete.Count > 0)
                {
                    return "Не заполнены книги: " + string.Join(", ", incomplete) +
                        ". Откройте книгу в списке и добавьте фотографии.";
                }

                if (SelectedFormat is null)
                    return "Выберите формат группы — по нему сайт считает цену.";

                if (!_parametersLoaded)
                    return "Читаем параметры формата на сайте — это займёт несколько секунд.";

                if (_parametersProblem is not null)
                    return _parametersProblem;

                if (!_parameters.IsAnswered)
                    return "Выберите параметры формата: " + _parameters.MissingText + ".";

                if (_isPricing)
                    return "Считаем цену на сайте…";

                if (_checkingFiles)
                    return "Проверяем файлы книг…";

                if (_priceProblem is not null)
                    return _priceProblem;

                if (_draftNote is not null)
                    return _draftNote;

                if (included.Any(book => book.IsFailed))
                    return "Исправьте отмеченные книги: откройте книгу в списке, чтобы увидеть причину.";

                if (_plan is { Problems.Count: > 0 } withProblems)
                    return string.Join(" ", withProblems.Problems);

                if (_plan is null)
                    return "Сайт не назвал цену — проверьте параметры формата.";

                if (NeedsConfirmation && !_confirmationsAccepted)
                    return "Отметьте, что файлы можно отправлять: край сайта может обрезать.";

                if (_files is null)
                    return _filesProblem ?? "Файлы ещё не проверены.";

                if (_files is { CanSend: false })
                    return "Файлы не готовы к отправке — исправьте отмеченное в книгах.";

                return CanGo ? string.Empty : "Заказ не готов к отправке.";
            }
        }

        /// <summary>Takes the formats the site offers and keeps the one the books carry.</summary>
        internal void ApplyFormats(IReadOnlyList<FormatChoice> formats)
        {
            _choices = formats ?? Array.Empty<FormatChoice>();

            foreach (var row in Books)
                row.RefreshFormats();

            // A group whose books already carry a format keeps it. A project assembled from two
            // folders keeps one stored format on both books, which is why the page split them by
            // their own page size and not by this: the first group still has to be told which
            // format it really is.
            _selected = Books.Select(row => row.SelectedFormat)
                .FirstOrDefault(choice => choice is not null);

            if (_selected is not null)
                ApplyFormatToBooks(_selected);

            OnPropertyChanged(nameof(Formats));
            OnPropertyChanged(nameof(HasFormats));
            OnPropertyChanged(nameof(SelectedFormat));
            OnPropertyChanged(nameof(SummaryText));

            RefreshReadiness();
        }

        /// <summary>Writes the formats' own answer on the group's format card.</summary>
        public void SetFormatNote(string? text)
        {
            var value = (text ?? string.Empty).Trim();
            if (value == _formatNote)
                return;

            _formatNote = value;
            OnPropertyChanged(nameof(FormatNote));
            OnPropertyChanged(nameof(HasFormatNote));
        }

        /// <summary>The formats' reading as the group's format card shows it.</summary>
        public string FormatNote => _formatNote;

        /// <summary>Whether the format card has a line about the format list itself.</summary>
        public bool HasFormatNote => _formatNote.Length > 0;

        /// <summary>Numbers the group after a book was moved and the order was laid out again.</summary>
        public void Renumber(int number)
        {
            if (Number == number)
                return;

            Number = number;
            OnPropertyChanged(nameof(Number));
            OnPropertyChanged(nameof(Title));
        }

        /// <summary>
        /// Takes a book moved into this group, with the group's format and answers.
        /// </summary>
        /// <remarks>
        /// A group is one order on the site: its books share one format and one set of answers.
        /// A book arriving from another group therefore takes the group's format and its stored
        /// answers instead of bringing its own, which would make the price describe a set the
        /// site is never asked to make.
        /// </remarks>
        internal void Add(OrderBookRow row)
        {
            if (row is null || _books.Contains(row))
                return;

            if (SelectedFormat is not null)
            {
                row.SelectedFormat = SelectedFormat;
            }
            else if (row.SelectedFormat is not null)
            {
                SelectedFormat = row.SelectedFormat;
            }

            var source = _books.FirstOrDefault();
            if (source is not null)
                CopyOptions(source.Book, row.Book);

            _books.Add(row);
            _books.Sort((left, right) => left.Number.CompareTo(right.Number));

            OnPropertyChanged(nameof(Books));
            OnPropertyChanged(nameof(SummaryText));
            RefreshReadiness();
        }

        /// <summary>Gives a book back to the page, which moves it to another group.</summary>
        internal bool Remove(OrderBookRow row)
        {
            if (!_books.Remove(row))
                return false;

            OnPropertyChanged(nameof(Books));
            OnPropertyChanged(nameof(SummaryText));
            RefreshReadiness();

            return _books.Count > 0;
        }

        private static void CopyOptions(CoreBook from, CoreBook to)
        {
            if (ReferenceEquals(from, to))
                return;

            to.ClearSiteOptions();

            foreach (var pair in from.SiteOptions)
                to.SetSiteOption(pair.Key, pair.Value);
        }
    }
}
