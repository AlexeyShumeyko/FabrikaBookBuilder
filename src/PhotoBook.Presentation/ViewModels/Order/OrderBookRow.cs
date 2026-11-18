using System;
using System.Collections.Generic;
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

        private FormatChoice? _selected;
        private string _priceText = "—";
        private bool _isExpanded;
        private string _statusText = string.Empty;
        private bool _isFailed;
        private string _reasonsText = string.Empty;
        private IReadOnlyList<UploadFileRow> _fileRows = Array.Empty<UploadFileRow>();

        public OrderBookRow(
            CoreBook book,
            int number,
            Func<IReadOnlyList<FormatChoice>> formats,
            Action<OrderBookRow> chosen)
        {
            _book = book;
            _number = number;
            _formats = formats;
            _chosen = chosen;
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

        /// <summary>Whether every slot holds a photograph.</summary>
        public bool IsReady => _book.IsValid;

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
        public string ConditionText => _statusText.Length > 0
            ? _statusText
            : IsReady
                ? "Готова"
                : _book.FilledSlotCount == 0
                    ? "Нет фотографий"
                    : "Не хватает фотографий";

        /// <summary>Whether the book needs attention, which the list shows in red.</summary>
        public bool IsFailed => _isFailed;

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

        /// <summary>What the check said about the book as a whole, one line each.</summary>
        public string ReasonsText => _reasonsText;

        /// <summary>Whether there is anything to read in the book's expansion.</summary>
        public bool HasReasons => _reasonsText.Length > 0;

        /// <summary>Takes the status and the files the check found, and opens a book that failed.</summary>
        public void ShowCheck(UploadBookRow check)
        {
            if (check is null)
                throw new ArgumentNullException(nameof(check));

            _statusText = check.StatusText;
            _isFailed = check.IsFailed;
            _reasonsText = check.ReasonsText;
            _fileRows = check.Files;
            _isExpanded = check.IsFailed;

            OnPropertyChanged(nameof(ConditionText));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(IsExpanded));
            OnPropertyChanged(nameof(FileRows));
            OnPropertyChanged(nameof(HasFileRows));
            OnPropertyChanged(nameof(ReasonsText));
            OnPropertyChanged(nameof(HasReasons));
        }

        /// <summary>Goes back to the book's own words, for when the plan no longer includes it.</summary>
        public void ClearCheck()
        {
            _statusText = string.Empty;
            _isFailed = false;
            _reasonsText = string.Empty;
            _fileRows = Array.Empty<UploadFileRow>();

            OnPropertyChanged(nameof(ConditionText));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(FileRows));
            OnPropertyChanged(nameof(HasFileRows));
            OnPropertyChanged(nameof(ReasonsText));
            OnPropertyChanged(nameof(HasReasons));
        }

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
            private set => SetProperty(ref _priceText, value ?? "—");
        }

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
