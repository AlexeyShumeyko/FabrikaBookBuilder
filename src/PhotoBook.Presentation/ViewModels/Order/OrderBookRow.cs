using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Files;
using CoreBook = PhotoBook.Core.Book;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>One book of the order as the page lists it, with its format and price.</summary>
    /// <remarks>
    /// The row reads the format list through the page rather than keeping a copy: the catalog is
    /// read after the rows are made, and a copy taken at construction would leave every row
    /// showing an empty picker.
    /// </remarks>
    public sealed class OrderBookRow : ViewModelBase
    {
        private readonly CoreBook _book;
        private readonly int _number;
        private readonly Func<IReadOnlyList<FormatChoice>> _formats;
        private readonly Action<OrderBookRow> _chosen;
        private readonly Action<OrderBookRow>? _exclusionChanged;

        private bool _excluded;
        private FormatChoice? _selected;
        private string _priceText = "—";
        private bool _isExpanded;
        private string _statusText = string.Empty;
        private bool _checkFailed;
        private string _checkReasons = string.Empty;
        private string _planProblem = string.Empty;
        private IReadOnlyList<UploadFileRow> _fileRows = Array.Empty<UploadFileRow>();
        private readonly ObservableCollection<BookMenuEntry> _menu = new();
        private bool _isMenuOpen;
        private bool _isGroupUnconfigured;

        public OrderBookRow(
            CoreBook book,
            int number,
            Func<IReadOnlyList<FormatChoice>> formats,
            Action<OrderBookRow> chosen,
            Action<OrderBookRow>? exclusionChanged = null)
        {
            _book = book;
            _number = number;
            _formats = formats;
            _chosen = chosen;
            _exclusionChanged = exclusionChanged;
        }

        /// <summary>The book behind the row, so the page can write choices into it.</summary>
        public CoreBook Book => _book;

        /// <summary>The book's place in the order, starting at one.</summary>
        public int Number => _number;

        /// <summary>Its name, or «Книга N» when it has none.</summary>
        public string Name => string.IsNullOrWhiteSpace(_book.Name)
            ? "Книга " + Number
            : _book.Name!.Trim();

        /// <summary>Spreads in the book, the cover not counted.</summary>
        public int Spreads => _book.Pages.Count(page => !page.IsCover);

        /// <summary>Slots holding a photo, the cover counted.</summary>
        public int Photos => _book.FilledSlotCount;

        /// <summary>
        /// The book's cover, as the small picture its row draws.
        /// </summary>
        /// <remarks>
        /// The photograph itself rather than a thumbnail path recorded beside it: the picture
        /// that is drawn picks the thumbnail up on its own when one exists, and a recorded path
        /// that has been cleaned up since would decode to nothing and leave the row with an
        /// empty frame - which reads as a book whose cover is missing.
        /// </remarks>
        public string? ThumbnailPath => _book.Cover is { IsEmpty: false } cover
            ? cover.SourcePath
            : null;

        /// <summary>Whether the row has a picture to draw.</summary>
        public bool HasThumbnail => ThumbnailPath is not null;

        /// <summary>Whether every slot holds a photograph.</summary>
        public bool IsReady => _book.IsValid;

        /// <summary>
        /// Whether the customer took this book out of the coming upload.
        /// </summary>
        /// <remarks>
        /// The book stays where it is on the page - in its group, with its format and its price -
        /// so the choice is visible and reversible. It is simply not sent, and a book that is not
        /// sent is not a book the site has to accept: an incomplete book that is excluded does
        /// not hold its group back.
        /// </remarks>
        public bool IsExcluded
        {
            get => _excluded;
            set
            {
                if (!SetProperty(ref _excluded, value))
                    return;

                NotifyBook();
                _exclusionChanged?.Invoke(this);
            }
        }

        /// <summary>Whether the book goes in the coming run.</summary>
        public bool IsActive => !IsExcluded;

        /// <summary>Whether the row reads as left out of the run.</summary>
        public bool IsLeftOut => IsExcluded;

        /// <summary>How the row is faded when it is not sent.</summary>
        public double RowOpacity => IsExcluded ? 0.55 : 1.0;

        /// <summary>What the row says instead of a status when it is excluded.</summary>
        public string ExclusionText => IsExcluded ? "Исключена из загрузки" : string.Empty;

        /// <summary>Takes the book out of the run, or puts it back in.</summary>
        public void ToggleExcluded() => IsExcluded = !IsExcluded;

        /// <summary>What the book still needs before it can go.</summary>
        public BookCondition Condition => IsReady
            ? BookCondition.Ready
            : _book.FilledSlotCount == 0
                ? BookCondition.NoPhotos
                : BookCondition.Incomplete;

        /// <summary>How the row describes the book's state.</summary>
        /// <remarks>
        /// Before the files are read this is the book's own answer - full or not. Once they are
        /// read it is the status the site's requirements give it, which is the word the customer
        /// needs before pressing send.
        /// </remarks>
        public string ConditionText => IsExcluded
            ? "Исключена из загрузки"
            : _planProblem.Length > 0 || IsGroupUnconfigured
                ? "Не готова"
                : _statusText.Length > 0
                    ? _statusText
                    : IsReady
                        ? "Готова"
                        : _book.FilledSlotCount == 0
                            ? "Нет фотографий"
                            : "Не хватает фотографий";

        /// <summary>Whether the book needs attention, which the list shows in red.</summary>
        public bool IsFailed => !IsExcluded && (_checkFailed || _planProblem.Length > 0);

        /// <summary>
        /// Whether the group this book stands in is not set up yet.
        /// </summary>
        /// <remarks>
        /// A book cannot be «Готова» in a group that is not set up: without a format or without
        /// its parameters answered the site has nothing to price and nothing to print, so the
        /// row says it is not ready in the warm tone the prototype gives an unconfigured group -
        /// not in the green of a book that passed.
        /// </remarks>
        public bool IsGroupUnconfigured
        {
            get => _isGroupUnconfigured;
            set
            {
                if (SetProperty(ref _isGroupUnconfigured, value))
                    NotifyBook();
            }
        }

        /// <summary>Whether the row's state is the warm «not set up» one.</summary>
        /// <remarks>
        /// One row shows one state: a book the site refused keeps its own red word even when
        /// its group is also unconfigured, and an excluded book keeps the quiet one.
        /// </remarks>
        public bool ShowsGroupUnconfigured =>
            IsGroupUnconfigured && !IsExcluded && !IsFailed;

        /// <summary>Whether the book goes and has nothing wrong with it.</summary>
        /// <remarks>
        /// The green state is the going book's own: a book the customer took out of the run is
        /// not a book that passed, and showing its «Исключена из загрузки» in the ready green
        /// was the same mistake as calling a failed book ready.
        /// </remarks>
        public bool IsFine => !IsExcluded && !IsFailed && !IsGroupUnconfigured;

        /// <summary>
        /// The one sentence under the book's name: why it cannot go.
        /// </summary>
        /// <remarks>
        /// The prototype writes the reason under the name, in red, so a book with a bad cover is
        /// found by reading the row rather than by opening it: the files are listed in full in
        /// the expansion, but the customer should not have to open a row to learn it is wrong.
        /// </remarks>
        public string ProblemText
        {
            get
            {
                if (IsExcluded)
                    return string.Empty;

                if (_planProblem.Length > 0)
                    return _planProblem;

                var file = _fileRows.FirstOrDefault(row => row.IsProblem);
                if (file?.Problem is { Length: > 0 } problem)
                    return problem;

                var first = _checkReasons.Split('\n')[0].Trim();
                return first;
            }
        }

        /// <summary>Whether the row has a reason to show under its name.</summary>
        public bool HasProblem => ProblemText.Length > 0;

        /// <summary>Whether the row's three-dot menu is open.</summary>
        public bool IsMenuOpen
        {
            get => _isMenuOpen;
            set => SetProperty(ref _isMenuOpen, value);
        }

        /// <summary>What the three-dot menu offers right now.</summary>
        /// <remarks>
        /// Rebuilt on every opening from the page's groups, so the menu can never offer a group
        /// the book already left, and it is empty while it is closed - which is also what keeps
        /// a stale list out of the row's layout.
        /// </remarks>
        public IReadOnlyList<BookMenuEntry> MenuEntries => _menu;

        /// <summary>Fills the menu and opens it.</summary>
        public void ShowMenu(IEnumerable<BookMenuEntry> entries)
        {
            _menu.Clear();

            foreach (var entry in entries)
                _menu.Add(entry);

            OnPropertyChanged(nameof(MenuEntries));
            IsMenuOpen = true;
        }

        /// <summary>Closes the menu, keeping its last entries until it is filled again.</summary>
        public void CloseMenu() => IsMenuOpen = false;

        /// <summary>Whether the book's files are listed under it.</summary>
        /// <remarks>
        /// Closed by default, and opened by the check when the book has something wrong: the
        /// customer's next question is which file, and a list that hid it behind a click would
        /// make them ask it for every book.
        /// </remarks>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        /// <summary>The files as the row would send them, the cover first.</summary>
        public IReadOnlyList<UploadFileRow> FileRows => _fileRows;

        /// <summary>Whether the book has files to list.</summary>
        public bool HasFileRows => _fileRows.Count > 0;

        /// <summary>What the book as a whole cannot go with, one line each.</summary>
        /// <remarks>
        /// Two sources, one place: the order's own reason (the site did not describe where the
        /// spreads go, the format takes another number of files) and the reading of the files.
        /// Both are about the book, so both belong in its expansion rather than in a sentence
        /// over every group.
        /// </remarks>
        public string ReasonsText => Join(_planProblem, _checkReasons);

        /// <summary>Whether there is anything to read in the book's expansion.</summary>
        public bool HasReasons => ReasonsText.Length > 0;

        /// <summary>Takes the status and the files the check found, and opens a book that failed.</summary>
        public void ShowCheck(UploadBookRow check)
        {
            if (check is null)
                throw new ArgumentNullException(nameof(check));

            _statusText = check.StatusText;
            _checkFailed = check.IsFailed;
            _checkReasons = check.ReasonsText;
            _fileRows = check.Files;

            // The row opens when the book needs an answer, which is more than a failure: a file
            // the site would trim keeps the book «Готова» and still has to be seen, and a row
            // that stayed closed left the customer with a disabled button and nothing to read.
            if (_planProblem.Length > 0 || check.IsExpanded)
                _isExpanded = true;

            NotifyBook();
        }

        /// <summary>Goes back to the book's own words, for when the plan no longer includes it.</summary>
        public void ClearCheck()
        {
            _statusText = string.Empty;
            _checkFailed = false;
            _checkReasons = string.Empty;
            _fileRows = Array.Empty<UploadFileRow>();

            NotifyBook();
        }

        /// <summary>
        /// Says why the order itself refuses this book, on the book's own row.
        /// </summary>
        /// <param name="text">The reason, already without the book's name.</param>
        /// <param name="expand">Whether to open the row, which a format problem deserves.</param>
        public void ShowProblem(string text, bool expand = false)
        {
            var value = (text ?? string.Empty).Trim();

            if (expand && value.Length > 0)
                _isExpanded = true;

            if (value == _planProblem)
            {
                if (expand && value.Length > 0)
                    OnPropertyChanged(nameof(IsExpanded));

                return;
            }

            _planProblem = value;
            NotifyBook();
        }

        /// <summary>Forgets the order's reason, for when the order changed and it no longer holds.</summary>
        public void ClearProblem()
        {
            if (_planProblem.Length == 0)
                return;

            _planProblem = string.Empty;
            NotifyBook();
        }

        private void NotifyBook()
        {
            OnPropertyChanged(nameof(ConditionText));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(IsExpanded));
            OnPropertyChanged(nameof(FileRows));
            OnPropertyChanged(nameof(HasFileRows));
            OnPropertyChanged(nameof(ReasonsText));
            OnPropertyChanged(nameof(HasReasons));
            OnPropertyChanged(nameof(ProblemText));
            OnPropertyChanged(nameof(HasProblem));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsLeftOut));
            OnPropertyChanged(nameof(IsFine));
            OnPropertyChanged(nameof(ShowsGroupUnconfigured));
            OnPropertyChanged(nameof(RowOpacity));
            OnPropertyChanged(nameof(ExclusionText));
        }

        private static string Join(params string[] lines) =>
            string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)));

        /// <summary>Everything the picker offers.</summary>
        public IReadOnlyList<FormatChoice> Formats => _formats();

        /// <summary>
        /// The format chosen for this book. Choosing one writes it into the book and forgets the
        /// parameters of the format chosen before, because those answers belong to it.
        /// </summary>
        public FormatChoice? SelectedFormat
        {
            get => _selected ??= Formats.FirstOrDefault(choice => choice.Matches(_book));
            set
            {
                if (ReferenceEquals(SelectedFormat, value)) return;

                var before = SelectedFormat?.Format.Id;
                _selected = value;

                if (value is not null)
                {
                    _book.SiteProductId = value.Product.Id;
                    _book.SiteVariantId = value.Format.Id;
                    _book.SiteProductTitle = value.Product.Name;
                    _book.SiteVariantTitle = value.Format.Name;
                }

                if (before != value?.Format.Id)
                    _book.ClearSiteOptions();

                OnPropertyChanged(nameof(SelectedFormat));
                OnPropertyChanged(nameof(FormatText));
                _chosen(this);
            }
        }

        /// <summary>The chosen format as the row shows it, or a note that none is chosen.</summary>
        public string FormatText => SelectedFormat?.DisplayText ?? "не выбран";

        /// <summary>The price the site named for this book.</summary>
        public string PriceText
        {
            get => _priceText;
            private set
            {
                if (SetProperty(ref _priceText, value ?? "—"))
                    OnPropertyChanged(nameof(IsPriceEmpty));
            }
        }

        /// <summary>Whether the row is still waiting for a price rather than showing one.</summary>
        public bool IsPriceEmpty => _priceText == "—";

        /// <summary>Clears whatever price was shown, for when a choice below changes.</summary>
        public void ForgetPrice() => PriceText = "—";

        /// <summary>Shows the price the site named for this book.</summary>
        public void ShowPrice(string text) => PriceText = string.IsNullOrWhiteSpace(text) ? "—" : text;

        /// <summary>Tells the row the catalog arrived, so the picker fills in.</summary>
        public void RefreshFormats()
        {
            // Re-resolved from the list the picker is about to show: a choice kept from the list
            // read before would no longer be one of its items, and the window draws that as an
            // empty picker over a book whose format is chosen.
            _selected = Formats.FirstOrDefault(choice => choice.Matches(_book));

            OnPropertyChanged(nameof(Formats));
            OnPropertyChanged(nameof(SelectedFormat));
            OnPropertyChanged(nameof(FormatText));
        }
    }
}
