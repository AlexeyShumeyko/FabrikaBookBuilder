using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Files;
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
    /// The price is asked for by the page itself, once the answers are complete: the customer
    /// should not have to press «рассчитать» to learn what the order costs. It is asked once per
    /// settled set of answers rather than on every keystroke, because the platform is shared and
    /// one book is one request: a page that called the site while the answers were still being
    /// filled would spend requests on sets nobody chose.
    /// </para>
    /// </remarks>
    public sealed partial class SiteOrderViewModel : ViewModelBase
    {
        private readonly ISiteCatalogService? _catalog;
        private readonly ISiteSessionStore? _sessions;
        private readonly OrderPriceService? _prices;
        private readonly SiteOrderParameters _parameters;
        private readonly OrderFileCheckService? _fileCheck;

        private IReadOnlyList<OrderBookRow> _books = Array.Empty<OrderBookRow>();
        private IReadOnlyList<FormatChoice> _formats = Array.Empty<FormatChoice>();
        private FormatChoice? _selectedFormat;
        private bool _applyingFormat;
        private bool _parametersLoaded;
        private readonly SiteOrderUpload _upload;

        private CoreProject? _project;
        private string _projectName = string.Empty;
        private int _spreadCount;
        private string _message = string.Empty;
        private string _totalText = string.Empty;
        private bool _isReady;
        private bool _isLoadingFormats;

        public SiteOrderViewModel(
            ISiteCatalogService? catalog = null,
            ISiteSessionStore? sessions = null,
            IGroupQuoteService? quotes = null,
            IFileService? files = null,
            OrderFileCheckService? fileCheck = null)
        {
            _catalog = catalog;
            _sessions = sessions;
            _fileCheck = fileCheck;

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

        /// <summary>The formats the site sells, in the site's order.</summary>
        public IReadOnlyList<FormatChoice> Formats => _formats;

        /// <summary>Whether the site named any format at all.</summary>
        public bool HasFormats => _formats.Count > 0;

        /// <summary>
        /// The format of the group: one choice for every book in it.
        /// </summary>
        /// <remarks>
        /// A group is one project on the site, and a project holds books of one format and one
        /// set of parameters. The choice therefore belongs to the page rather than to each row:
        /// ten rows with ten pickers invited ten answers the site would price separately, and
        /// the order page has no way to send a project with two formats in it anyway. Picking
        /// once writes the format into every book of the group.
        /// </remarks>
        public FormatChoice? SelectedFormat
        {
            get => _selectedFormat ?? Books.Select(row => row.SelectedFormat)
                .FirstOrDefault(choice => choice is not null);
            set
            {
                if (Equals(SelectedFormat, value))
                    return;

                _selectedFormat = value;
                OnPropertyChanged(nameof(SelectedFormat));
                OnPropertyChanged(nameof(ChosenFormatText));

                ApplyFormatToBooks(value);
                AfterFormatChosen();
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
        public bool CanUpload => !_isPricing && _upload.CanUpload && FilesAllow;

        /// <summary>The group's format, as the control panel shows it.</summary>
        public string ChosenFormatText => SelectedFormat?.DisplayText ?? "не выбран";

        /// <summary>
        /// Reads the project into the page. A project that is not assembled comes back with the
        /// books still listed and the reason why nothing can be sent.
        /// </summary>
        public void Prepare(CoreProject? project, string? projectName = null)
        {
            _project = project;
            _selectedFormat = null;

            // The questions have not been read yet, and an empty list of questions is not the
            // same thing as a format that asks none: without this a price was asked for before
            // the questions arrived, and the page showed a total and an enabled button over
            // three unanswered questions.
            _parametersLoaded = false;
            OnPropertyChanged(nameof(SelectedFormat));

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
                ForgetFiles();
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
        /// <remarks>
        /// A new plan is a new set of files to send, so the files are read again for it: the
        /// formats and their answers decide what the site asks of a page, and a check made for
        /// the answers before this one would be about a different order.
        /// </remarks>
        private void SetPlan(UploadPlan? plan)
        {
            _upload.Offer(plan);

            if (plan is null)
            {
                ForgetFiles();
                return;
            }

            ForgetFiles();
            _filesRun = CheckFilesAsync();
        }

        private void ApplyFormats(IReadOnlyList<CatalogProduct> products)
        {
            _formats = products
                .SelectMany(product => product.Formats.Select(format => new FormatChoice(product, format)))
                .ToList();

            foreach (var book in Books)
                book.RefreshFormats();

            // A project whose books already carry a format keeps it, and the whole group follows
            // the first one: the page no longer offers a per-book choice, so two formats left in
            // one project would be a group the site cannot be asked to make.
            _selectedFormat = Books.Select(row => row.SelectedFormat)
                .FirstOrDefault(choice => choice is not null);

            if (_selectedFormat is not null)
                ApplyFormatToBooks(_selectedFormat);

            if (_formats.Count == 0)
                Message = "Сайт не предложил ни одного формата.";

            OnPropertyChanged(nameof(Formats));
            OnPropertyChanged(nameof(HasFormats));
            OnPropertyChanged(nameof(SelectedFormat));
            OnPropertyChanged(nameof(ChosenFormatText));
            RefreshReadiness();
        }

        private async Task LoadParametersAsync()
        {
            _parametersLoaded = false;

            await _parameters.LoadAsync(Books).ConfigureAwait(true);

            if (_parameters.Problem is { Length: > 0 } problem)
                Message = problem;

            _parametersLoaded = true;

            OnPropertyChanged(nameof(HasParameters));
            RefreshReadiness();
        }

        private void OnFormatChosen(OrderBookRow row)
        {
            // A row written by the page itself is the group's choice being spread over the
            // books, not a new answer: it must not start a second price run per book.
            if (_applyingFormat)
                return;

            _selectedFormat = row.SelectedFormat;
            OnPropertyChanged(nameof(SelectedFormat));
            OnPropertyChanged(nameof(ChosenFormatText));

            ApplyFormatToBooks(_selectedFormat);
            AfterFormatChosen();
        }

        /// <summary>Writes the group's format into every book, without asking again.</summary>
        private void ApplyFormatToBooks(FormatChoice? choice)
        {
            _applyingFormat = true;
            try
            {
                foreach (var row in Books)
                    row.SelectedFormat = choice;
            }
            finally
            {
                _applyingFormat = false;
            }
        }

        /// <summary>What a new format means for the order: no price, no plan, new questions.</summary>
        private void AfterFormatChosen()
        {
            // A new format asks new questions, so the order is not ready until they are read.
            _parametersLoaded = false;
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
                _parametersLoaded &&
                _parameters.IsAnswered;

            OnPropertyChanged(nameof(ChosenFormatText));

            PriceIfReady();
        }


    }
}
