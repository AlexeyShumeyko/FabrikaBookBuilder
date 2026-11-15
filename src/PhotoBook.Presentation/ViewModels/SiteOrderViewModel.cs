using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Catalog;
using CoreBook = PhotoBook.Core.Book;
using CoreProject = PhotoBook.Core.Project;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>One format the customer can pick for a book.</summary>
    /// <param name="Product">The product it belongs to.</param>
    /// <param name="Format">The product's format.</param>
    public sealed record FormatChoice(CatalogProduct Product, CatalogFormat Format)
    {
        /// <summary>The line the picker shows: «Лайфлат 21x30».</summary>
        public string DisplayText => Product.Name + " " + Format.Name;
    }

    /// <summary>One book of the order as the page lists it, with its format.</summary>
    /// <remarks>
    /// The row reads the format list through the page rather than keeping a copy: the catalog
    /// is read after the rows are made, and a copy taken at construction would leave every row
    /// showing an empty picker.
    /// </remarks>
    public sealed class OrderBookRow : ViewModelBase
    {
        private readonly CoreBook _book;
        private readonly int _number;
        private readonly Func<IReadOnlyList<FormatChoice>> _formats;
        private readonly Action<OrderBookRow> _chosen;

        private FormatChoice? _selected;

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
        public string ConditionText => IsReady
            ? "Готова"
            : _book.FilledSlotCount == 0
                ? "Нет фотографий"
                : "Не хватает фотографий";

        /// <summary>Everything the picker offers.</summary>
        public IReadOnlyList<FormatChoice> Formats => _formats();

        /// <summary>
        /// The format chosen for this book. Choosing one writes it into the book, so the choice
        /// is saved with the project rather than living in the window.
        /// </summary>
        public FormatChoice? SelectedFormat
        {
            get => _selected ??= Formats.FirstOrDefault(choice =>
                _book.SiteProductId == choice.Product.Id && _book.SiteVariantId == choice.Format.Id);
            set
            {
                if (ReferenceEquals(SelectedFormat, value)) return;

                _selected = value;

                if (value is not null)
                {
                    _book.SiteProductId = value.Product.Id;
                    _book.SiteVariantId = value.Format.Id;
                    _book.SiteProductTitle = value.Product.Name;
                    _book.SiteVariantTitle = value.Format.Name;
                }

                OnPropertyChanged(nameof(SelectedFormat));
                OnPropertyChanged(nameof(FormatText));
                _chosen(this);
            }
        }

        /// <summary>The chosen format as the row shows it, or a note that none is chosen.</summary>
        public string FormatText => SelectedFormat?.DisplayText ?? "не выбран";

        /// <summary>Tells the row the catalog arrived, so the picker fills in.</summary>
        public void RefreshFormats()
        {
            OnPropertyChanged(nameof(Formats));
            OnPropertyChanged(nameof(SelectedFormat));
            OnPropertyChanged(nameof(FormatText));
        }
    }

    /// <summary>
    /// The order page: the project's books, their formats, what is wrong and the way on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The page is reached from the project's own panel, beside export, and only a fully
    /// assembled project gets there: an order assembled from a half-filled book would be a
    /// project on the site that nobody meant to make.
    /// </para>
    /// <para>
    /// What the page shows is read from the project every time it is opened, not stored, and a
    /// chosen format is written into the book itself: the project is where the order lives, so
    /// closing the window must not lose what was picked.
    /// </para>
    /// </remarks>
    public sealed class SiteOrderViewModel : ViewModelBase
    {
        private readonly ISiteCatalogService? _catalog;
        private readonly ISiteSessionStore? _sessions;

        private IReadOnlyList<OrderBookRow> _books = Array.Empty<OrderBookRow>();
        private IReadOnlyList<FormatChoice> _formats = Array.Empty<FormatChoice>();
        private string _projectName = string.Empty;
        private int _spreadCount;
        private string _message = string.Empty;
        private bool _isReady;
        private bool _isLoadingFormats;

        public SiteOrderViewModel(ISiteCatalogService? catalog = null, ISiteSessionStore? sessions = null)
        {
            _catalog = catalog;
            _sessions = sessions;
        }

        /// <summary>Raised when the customer asks to go back to the project.</summary>
        public event EventHandler? BackRequested;

        /// <summary>
        /// Raised when a format was chosen, so the shell can save the project the choice
        /// belongs to.
        /// </summary>
        public event EventHandler? FormatChosen;

        /// <summary>The books of the order, in the project's order.</summary>
        public IReadOnlyList<OrderBookRow> Books
        {
            get => _books;
            private set
            {
                if (SetProperty(ref _books, value))
                {
                    OnPropertyChanged(nameof(BookCountText));
                    OnPropertyChanged(nameof(ChosenFormatText));
                }
            }
        }

        /// <summary>How many books are going. The label under it names them.</summary>
        public string BookCountText => Books.Count.ToString(CultureInfo.CurrentCulture);

        /// <summary>The project's name, as the header shows it.</summary>
        public string ProjectName
        {
            get => _projectName;
            private set => SetProperty(ref _projectName, value ?? string.Empty);
        }

        /// <summary>Spreads across the order, which is what the site counts.</summary>
        public int SpreadCount
        {
            get => _spreadCount;
            private set => SetProperty(ref _spreadCount, value);
        }

        /// <summary>What the customer has to know before going on.</summary>
        public string Message
        {
            get => _message;
            private set
            {
                if (SetProperty(ref _message, value ?? string.Empty))
                    OnPropertyChanged(nameof(HasMessage));
            }
        }

        /// <summary>Whether there is anything to say.</summary>
        public bool HasMessage => _message.Length > 0;

        /// <summary>Whether every book has a format and is full.</summary>
        public bool IsReady
        {
            get => _isReady;
            private set => SetProperty(ref _isReady, value);
        }

        /// <summary>Whether the catalog is being read right now.</summary>
        public bool IsLoadingFormats
        {
            get => _isLoadingFormats;
            private set => SetProperty(ref _isLoadingFormats, value);
        }

        /// <summary>How many books still need a format, in words.</summary>
        public string ChosenFormatText
        {
            get
            {
                if (Books.Count == 0) return "нет книг";

                var missing = Books.Count(book => book.SelectedFormat is null);

                return missing == 0 ? "у всех книг выбран формат" : "без формата: " + missing;
            }
        }

        /// <summary>
        /// Reads the project into the page. A project that is not assembled comes back with the
        /// books still listed and the reason why nothing can be sent.
        /// </summary>
        public void Prepare(CoreProject? project, string? projectName = null)
        {
            if (project is null)
            {
                Books = Array.Empty<OrderBookRow>();
                ProjectName = string.Empty;
                SpreadCount = 0;
                IsReady = false;
                Message = "Проект не открыт.";
                return;
            }

            var inventory = BookInventory.From(project);

            Books = project.Books
                .Select((book, index) => new OrderBookRow(book, index + 1, () => _formats, OnFormatChosen))
                .ToList();

            ProjectName = string.IsNullOrWhiteSpace(projectName) ? "Проект" : projectName!.Trim();
            SpreadCount = inventory.Books.Sum(book => book.Spreads);
            Message = Problems(inventory);

            RefreshReadiness();
        }

        /// <summary>
        /// Reads the formats the site offers and hands them to every row.
        /// </summary>
        /// <remarks>
        /// One read for the page rather than one per book: the list is the same for all of them,
        /// and asking per row would multiply the same request by the size of the order.
        /// </remarks>
        public async Task LoadFormatsAsync()
        {
            if (_catalog is null || _sessions is null)
                return;

            var session = _sessions.Load();
            if (session is null)
            {
                Message = "Выбор формата появится после входа на сайт.";
                return;
            }

            IsLoadingFormats = true;
            try
            {
                var result = await _catalog.GetProductsAsync(session.Region).ConfigureAwait(true);

                if (!result.IsSuccess || result.Value is null)
                {
                    Message = result.Error ?? "Каталог сайта не открылся.";
                    return;
                }

                ApplyFormats(result.Value);
            }
            catch (Exception ex)
            {
                Message = "Каталог сайта не открылся: " + ex.Message;
            }
            finally
            {
                IsLoadingFormats = false;
            }
        }

        private void ApplyFormats(IReadOnlyList<CatalogProduct> products)
        {
            _formats = products
                .SelectMany(product => product.Formats.Select(format => new FormatChoice(product, format)))
                .ToList();

            foreach (var book in Books)
                book.RefreshFormats();

            if (_formats.Count == 0)
            {
                Message = "Сайт не предложил ни одного формата.";
            }
            else if (!HasMessage)
            {
                Message = "Выберите формат для каждой книги — цена считается по нему.";
            }

            OnPropertyChanged(nameof(ChosenFormatText));
            RefreshReadiness();
        }

        /// <summary>Says something on the page that came from outside it, such as a failed save.</summary>
        public void ShowMessage(string text) => Message = text ?? string.Empty;

        /// <summary>Goes back to the project the page was opened from.</summary>
        public void Back() => BackRequested?.Invoke(this, EventArgs.Empty);

        private void OnFormatChosen(OrderBookRow row)
        {
            RefreshReadiness();
            FormatChosen?.Invoke(this, EventArgs.Empty);
        }

        private void RefreshReadiness()
        {
            IsReady = Books.Count > 0 &&
                Books.All(book => book.IsReady && book.SelectedFormat is not null);

            OnPropertyChanged(nameof(ChosenFormatText));
        }

        /// <summary>Why the order cannot go, or the note that says what comes next.</summary>
        private string Problems(BookInventory inventory)
        {
            var lines = new List<string>();

            foreach (var book in inventory.Books.Where(book => !book.CanUpload))
            {
                lines.Add(book.Name + ": " +
                    (book.Condition == BookCondition.NoPhotos
                        ? "нет ни одной фотографии."
                        : "заполнено " + book.Photos + " из " + book.Slots + " слотов."));
            }

            if (inventory.Books.Count == 0)
                lines.Add("В проекте нет ни одной книги.");

            if (inventory.LeftoverPhotos > 0)
            {
                lines.Add("Фотографий вне книги: " + inventory.LeftoverPhotos +
                    " — они не попадут на сайт.");
            }

            if (lines.Count == 0)
            {
                lines.Add("Выберите формат для каждой книги — цена считается по нему.");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
