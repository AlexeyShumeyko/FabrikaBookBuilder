using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Order;
using PhotoBook.Application.Sites.Books.Pricing;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Application.Sites.Upload;
using CoreProject = PhotoBook.Core.Project;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>
    /// The order page: the project's books, their formats and parameters, the price and the way
    /// on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The page is reached from the project's own panel, beside export, and only a fully
    /// assembled project gets there: an order assembled from a half-filled book would be a
    /// project on the site that nobody meant to make.
    /// </para>
    /// <para>
    /// What the page shows is read from the project every time it is opened, not stored, and a
    /// chosen format and its parameters are written into the book itself: the project is where
    /// the order lives, so closing the window must not lose what was picked.
    /// </para>
    /// <para>
    /// The price is asked for on the customer's command rather than on every keystroke. The
    /// platform is shared, one book is one request, and a page that repriced itself as the
    /// choices changed would spend a request per click for a number nobody asked for yet.
    /// </para>
    /// </remarks>
    public sealed class SiteOrderViewModel : ViewModelBase
    {
        private readonly ISiteCatalogService? _catalog;
        private readonly ISiteSessionStore? _sessions;
        private readonly OrderPriceService? _prices;
        private readonly SiteOrderParameters _parameters;

        private IReadOnlyList<OrderBookRow> _books = Array.Empty<OrderBookRow>();
        private IReadOnlyList<FormatChoice> _formats = Array.Empty<FormatChoice>();
        private readonly SiteOrderUpload _upload;

        private CoreProject? _project;
        private string _projectName = string.Empty;
        private int _spreadCount;
        private string _message = string.Empty;
        private string _totalText = string.Empty;
        private bool _isReady;
        private bool _isLoadingFormats;
        private bool _isPricing;

        public SiteOrderViewModel(
            ISiteCatalogService? catalog = null,
            ISiteSessionStore? sessions = null,
            IGroupQuoteService? quotes = null,
            IFileService? files = null)
        {
            _catalog = catalog;
            _sessions = sessions;

            // The price is the order service's job; the page only carries its answer. The two
            // ports are given together because one without the other can price nothing.
            _prices = quotes is not null && files is not null
                ? new OrderPriceService(files, quotes)
                : null;

            _upload = new SiteOrderUpload(() => OnPropertyChanged(nameof(CanUpload)));
            _upload.Requested += (_, _) => UploadRequested?.Invoke(this, EventArgs.Empty);

            _parameters = new SiteOrderParameters(catalog, sessions);
            _parameters.Answered += (_, _) => OnAnswerChanged();
            _parameters.Blocks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasParameters));
        }

        /// <summary>Raised when the customer asks to go back to the project.</summary>
        public event EventHandler? BackRequested;

        /// <summary>
        /// Raised when a choice was made, so the shell can save the project it belongs to.
        /// </summary>
        public event EventHandler? ChoiceChanged;

        /// <summary>
        /// Raised when the customer asks to send the order, which is the only thing that starts
        /// a write. The plan is in <see cref="PendingUpload"/>.
        /// </summary>
        public event EventHandler? UploadRequested;

        /// <summary>The plan the upload screen is to send, once the customer has asked for it.</summary>
        public UploadPlan? PendingUpload => _upload.Pending;

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

        /// <summary>The parameters of each format chosen in the order.</summary>
        /// <remarks>
        /// The collection belongs to the parameters collaborator and is updated there; the page
        /// re-reads it for the view rather than replacing it, because a list swapped while the
        /// window arranges its panels is what crashed the page once.
        /// </remarks>
        public ObservableCollection<OrderFormatParameters> Parameters => _parameters.Blocks;

        /// <summary>Whether there is anything to answer.</summary>
        public bool HasParameters => Parameters.Count > 0;

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

        /// <summary>Whether every book is full, has a format and its parameters answered.</summary>
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

        /// <summary>Whether the site is being asked for a price right now.</summary>
        public bool IsPricing
        {
            get => _isPricing;
            private set
            {
                if (!SetProperty(ref _isPricing, value))
                    return;

                // A price run also settles whether the order may be sent: the plan it builds
                // is what enables the button, and the button was staying grey because only the
                // price button was told the run had finished.
                OnPropertyChanged(nameof(CanPrice));
                OnPropertyChanged(nameof(CanUpload));
            }
        }

        /// <summary>Whether asking for a price would reach the site with a complete order.</summary>
        public bool CanPrice =>
            !_isPricing && _prices is not null && Books.Count > 0 &&
            Books.All(book => book.SelectedFormat is not null);

        /// <summary>The order's total as the site wrote it, empty until it named one.</summary>
        public string TotalText
        {
            get => _totalText;
            private set
            {
                if (SetProperty(ref _totalText, value ?? string.Empty))
                    OnPropertyChanged(nameof(HasTotal));
            }
        }

        /// <summary>Whether the site has named a total.</summary>
        public bool HasTotal => _totalText.Length > 0;

        /// <summary>
        /// Whether the order can be sent: priced, complete and with every book's integration
        /// known. Sending is a write, so nothing but a finished order reaches the button.
        /// </summary>
        public bool CanUpload => !_isPricing && _upload.CanUpload;

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
            _project = project;

            if (project is null)
            {
                Books = Array.Empty<OrderBookRow>();
                _parameters.Clear();
                OnPropertyChanged(nameof(HasParameters));
                ProjectName = string.Empty;
                SpreadCount = 0;
                IsReady = false;
                TotalText = string.Empty;
                Message = "Проект не открыт.";
                return;
            }

            var inventory = BookInventory.From(project);

            Books = project.Books
                .Select((book, index) => new OrderBookRow(book, index + 1, () => _formats, OnFormatChosen))
                .ToList();

            ProjectName = string.IsNullOrWhiteSpace(projectName) ? "Проект" : projectName!.Trim();
            SpreadCount = inventory.Books.Sum(book => book.Spreads);
            TotalText = string.Empty;
            Message = ProjectProblems.Of(inventory);

            RefreshReadiness();
        }

        /// <summary>
        /// Reads the formats the site offers and the parameters of the ones already chosen.
        /// </summary>
        /// <remarks>
        /// One read for the page rather than one per book: the list is the same for all of them,
        /// and asking per row would multiply the same request by the size of the order. The
        /// parameters come from the uploader's document for each chosen format, kept by the
        /// catalog service, so a second visit to the page asks the site nothing.
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
                await LoadParametersAsync().ConfigureAwait(true);
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

        /// <summary>
        /// Asks the site for the order's price and shows it book by book.
        /// </summary>
        /// <remarks>
        /// The books travel as the order itself, not as the page's own idea of it: the same
        /// assembly that the upload will use builds the request, so the price shown is the price
        /// of what would be sent. A book that cannot be assembled is reported by name, and the
        /// rest of the order is still priced - the total is worth having even when one book
        /// needs an answer.
        /// </remarks>
        public async Task RecalculateAsync()
        {
            if (_catalog is null || _sessions is null || _prices is null || _project is null)
                return;

            var session = _sessions.Load();
            if (session is null)
            {
                Message = "Цена считается после входа на сайт.";
                return;
            }

            IsPricing = true;
            try
            {
                var read = await ChosenFormatDocuments
                    .ReadAsync(_catalog, session.Region, Books.Select(row => row.SelectedFormat))
                    .ConfigureAwait(true);

                if (!read.IsSuccess || read.Value is null)
                {
                    Message = read.Error ?? "Описание формата не открылось.";
                    return;
                }

                var formats = read.Value;

                foreach (var row in Books)
                    row.ForgetPrice();

                TotalText = string.Empty;

                var priced = await _prices
                    .PriceAsync(session, _project, formats)
                    .ConfigureAwait(true);

                if (priced.Quote is null)
                {
                    Message = string.Join(Environment.NewLine, priced.Problems);
                    return;
                }

                for (var index = 0; index < Books.Count && index < priced.Quote.Books.Count; index++)
                {
                    var book = priced.Quote.Books[index];
                    Books[index].ShowPrice(book.IsPriced ? book.Check.Quote!.Formatted : "не названа");
                }

                if (priced.IsPriced)
                {
                    TotalText = priced.TotalText;
                    Message = string.Empty;
                }
                else
                {
                    Message = string.Join(Environment.NewLine, priced.Problems);
                }

                // The plan is built from the answer that was just priced, so the button sends
                // exactly what the customer saw a total for.
                SetPlan(UploadPlans.Build(session.Region, priced.Draft, priced.Quote, formats));
            }
            catch (Exception ex)
            {
                Message = "Цена не получена: " + ex.Message;
            }
            finally
            {
                IsPricing = false;
            }
        }

        /// <summary>Says something on the page that came from outside it, such as a failed save.</summary>
        public void ShowMessage(string text) => Message = text ?? string.Empty;

        /// <summary>Goes back to the project the page was opened from.</summary>
        public void Back() => BackRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// Hands the priced order to the shell, which opens the upload screen. Nothing is sent
        /// until that screen's own button is pressed.
        /// </summary>
        public void Upload() => _upload.Request();

        /// <summary>Keeps the button and the plan in step with what the page knows.</summary>
        private void SetPlan(UploadPlan? plan) => _upload.Offer(plan);

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

        private async Task LoadParametersAsync()
        {
            await _parameters.LoadAsync(Books).ConfigureAwait(true);

            if (_parameters.Problem is { Length: > 0 } problem)
                Message = problem;

            OnPropertyChanged(nameof(HasParameters));
            RefreshReadiness();
        }

        private void OnFormatChosen(OrderBookRow row)
        {
            RefreshReadiness();
            TotalText = string.Empty;
            SetPlan(null);

            foreach (var book in Books)
                book.ForgetPrice();

            ChoiceChanged?.Invoke(this, EventArgs.Empty);
            _ = LoadParametersForChosenAsync();
        }

        private async Task LoadParametersForChosenAsync()
        {
            try
            {
                await LoadParametersAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Message = "Параметры формата не открылись: " + ex.Message;
            }
        }

        private void OnAnswerChanged()
        {
            foreach (var row in Books)
                row.ForgetPrice();

            TotalText = string.Empty;
            SetPlan(null);
            ChoiceChanged?.Invoke(this, EventArgs.Empty);
            RefreshReadiness();
        }

        private void RefreshReadiness()
        {
            IsReady = Books.Count > 0 &&
                Books.All(book => book.IsReady && book.SelectedFormat is not null) &&
                _parameters.IsAnswered;

            OnPropertyChanged(nameof(ChosenFormatText));
            OnPropertyChanged(nameof(CanPrice));
        }

    }
}
