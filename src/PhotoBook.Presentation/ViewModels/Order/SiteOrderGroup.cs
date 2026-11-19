using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Files;
using PhotoBook.Application.Sites.Books.Order;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Application.Sites.Projects;
using PhotoBook.Application.Sites.Upload;
using CoreBook = PhotoBook.Core.Book;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>
    /// One group of the order: the books that would go to the site as one project, with their
    /// format, their parameters, their price and their files.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A group exists because a project on the site holds one format. A program project may hold
    /// books of two formats - two folders assembled side by side - and the site cannot receive
    /// them as one order, so the page lays them out as separate groups before anything is chosen.
    /// Each group then answers for itself: its own format, its own parameters, its own price, and
    /// its own reading of its files. One group being wrong must not take the others down with it.
    /// </para>
    /// <para>
    /// The format is chosen once for the group and written into every book of it, because that is
    /// what the site's project is: one product, one format, several books. Ten rows with ten
    /// pickers would invite ten answers the site is never asked to price.
    /// </para>
    /// <para>
    /// The price is asked for by the group itself once its answers are complete, and the plan it
    /// builds is the group's own: what the page sends is the plans of the groups that can go, so
    /// a send is never one order assembled twice.
    /// </para>
    /// </remarks>
    public sealed partial class SiteOrderGroup : ViewModelBase
    {
        private readonly ISiteCatalogService? _catalog;
        private readonly ISiteSessionStore? _sessions;
        private readonly OrderPriceService? _prices;
        private readonly OrderFileCheckService? _fileCheck;
        private readonly SiteOrderParameters _parameters;
        private readonly IUploadedProjectStore? _uploaded;
        private readonly Action _changed;
        private readonly Action _choiceChanged;
        private readonly List<OrderBookRow> _books;
        private readonly string _projectName;

        private IReadOnlyList<FormatChoice> _choices = Array.Empty<FormatChoice>();
        private FormatChoice? _selected;
        private bool _applyingFormat;
        private bool _parametersLoaded;
        private string? _parametersProblem;
        private string? _priceProblem;
        private string? _draftNote;
        private string? _filesProblem;
        private string _formatNote = string.Empty;
        private bool _isReady;
        private bool _isPricing;
        private string _totalText = string.Empty;
        private Task _priceRun = Task.CompletedTask;
        private bool _repriceAfterRun;
        private UploadPlan? _plan;
        private long _uploadedProjectId;
        private bool _excluded;
        private bool _exclusionChosen;

        private OrderFileCheck? _files;
        private bool _checkingFiles;
        private bool _confirmationsAccepted;
        private string _filesText = string.Empty;
        private CancellationTokenSource? _cancelFiles;
        private Task _filesRun = Task.CompletedTask;

        public SiteOrderGroup(
            int number,
            IReadOnlyList<OrderBookRow> books,
            ISiteCatalogService? catalog,
            ISiteSessionStore? sessions,
            OrderPriceService? prices,
            OrderFileCheckService? fileCheck,
            Action changed,
            Action choiceChanged,
            IUploadedProjectStore? uploaded = null,
            string? projectName = null)
        {
            _uploaded = uploaded;
            _projectName = projectName ?? string.Empty;
            Number = number;
            _books = books is null ? new List<OrderBookRow>() : new List<OrderBookRow>(books);
            _catalog = catalog;
            _sessions = sessions;
            _prices = prices;
            _fileCheck = fileCheck;
            _changed = changed ?? throw new ArgumentNullException(nameof(changed));
            _choiceChanged = choiceChanged ?? throw new ArgumentNullException(nameof(choiceChanged));

            _parameters = new SiteOrderParameters(catalog, sessions);
            _parameters.Answered += (_, _) => OnAnswerChanged();
            _parameters.Blocks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasParameters));
        }

        /// <summary>The group's place in the order, starting at one.</summary>
        public int Number { get; private set; }

        /// <summary>What the group's card is headed with.</summary>
        public string Title => "Группа " + Number;

        /// <summary>The books of this group, in the project's order.</summary>
        /// <remarks>
        /// The list is the group's own and can change: the customer may move a book to another
        /// group when the page's own split put a stray book beside books it does not share
        /// parameters with. The rows keep the project's numbering, so a move changes the group
        /// and not the name of the book.
        /// </remarks>
        public IReadOnlyList<OrderBookRow> Books => _books;

        /// <summary>The books of the group that go in the coming run.</summary>
        /// <remarks>
        /// A book the customer excluded stays in the group and keeps its row, its format and its
        /// price, so the choice can be seen and undone; it is simply not sent, priced or
        /// checked. A group whose books are all excluded sends nothing.
        /// </remarks>
        public IReadOnlyList<OrderBookRow> IncludedBooks =>
            _books.Where(book => !book.IsExcluded).ToList();

        /// <summary>Whether the customer took this group out of the coming upload.</summary>
        public bool IsExcluded
        {
            get => _excluded;
            set
            {
                if (_excluded == value)
                    return;

                // A tick is the customer's answer, and it is never overwritten by the record;
                // a group put back in stays in for the rest of the visit.
                _exclusionChosen = true;
                SetExcluded(value);
            }
        }

        /// <summary>Changes the tick without claiming the customer made the choice.</summary>
        private void SetExcluded(bool value)
        {
            if (_excluded == value)
                return;

            _excluded = value;
            OnPropertyChanged(nameof(IsExcluded));
            OnPropertyChanged(nameof(IsIncluded));
            OnPropertyChanged(nameof(CanGo));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(HasStatus));

            NotifyProblem();
            _changed();
        }

        /// <summary>Whether the group goes in the coming run.</summary>
        public bool IsIncluded
        {
            get => !IsExcluded;
            set => IsExcluded = !value;
        }

        /// <summary>The project this group was last uploaded to, or zero.</summary>
        public long UploadedProjectId => _uploadedProjectId;

        /// <summary>Whether the group has already been uploaded once.</summary>
        public bool HasUploadedProject => _uploadedProjectId > 0;

        /// <summary>What the card says about an earlier upload.</summary>
        public string UploadedProjectText => _uploadedProjectId > 0
            ? "Эта группа уже была загружена на сайт — проект " + _uploadedProjectId + "."
            : string.Empty;

        /// <summary>The group in one line: how many books, in what format.</summary>
        public string SummaryText =>
            (IncludedBooks.Count == 1 ? "1 книга" : IncludedBooks.Count + " книги") +
            " · " + FormatText;

        /// <summary>The group's format as the page reads it, or a note that none is chosen.</summary>
        public string FormatText => SelectedFormat?.DisplayText ?? "не выбран";

        /// <summary>The formats the site sells, in the site's order.</summary>
        public IReadOnlyList<FormatChoice> Formats => _choices;

        /// <summary>Whether the site named any format at all.</summary>
        public bool HasFormats => _choices.Count > 0;

        /// <summary>
        /// The format of the group: one choice for every book in it.
        /// </summary>
        public FormatChoice? SelectedFormat
        {
            get => _selected ?? Books.Select(row => row.SelectedFormat)
                .FirstOrDefault(choice => choice is not null);
            set
            {
                if (Equals(SelectedFormat, value))
                    return;

                _selected = value;
                OnPropertyChanged(nameof(SelectedFormat));
                OnPropertyChanged(nameof(SummaryText));

                ApplyFormatToBooks(value);
                AfterFormatChosen();
            }
        }

        /// <summary>The parameters of the group's format, as the site asks them.</summary>
        public ObservableCollection<OrderFormatParameters> Parameters => _parameters.Blocks;

        /// <summary>Whether there is anything to answer.</summary>
        public bool HasParameters => Parameters.Count > 0;

        /// <summary>Whether every book is full, has a format and its parameters answered.</summary>
        public bool IsReady
        {
            get => _isReady;
            private set
            {
                if (SetProperty(ref _isReady, value))
                    NotifyProblem();
            }
        }

        /// <summary>Whether the site is being asked about this group right now.</summary>
        public bool IsPricing
        {
            get => _isPricing;
            private set
            {
                if (!SetProperty(ref _isPricing, value))
                    return;

                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(CanGo));
                NotifyProblem();
                _changed();
            }
        }

        /// <summary>Whether the group is waiting for an answer from the site or from the disk.</summary>
        public bool IsBusy => _isPricing || _checkingFiles;

        /// <summary>The price the group is asking for right now, so a reader can wait for it.</summary>
        public Task Pricing => _priceRun;

        /// <summary>The group's price as the site wrote it, empty until it named one.</summary>
        public string TotalText
        {
            get => _totalText;
            private set
            {
                if (SetProperty(ref _totalText, value ?? string.Empty))
                    OnPropertyChanged(nameof(HasTotal));
            }
        }

        /// <summary>Whether the site has named a price for this group.</summary>
        public bool HasTotal => _totalText.Length > 0;

        /// <summary>The group's price as a number, for the order's own sum.</summary>
        public decimal TotalAmount { get; private set; }

        /// <summary>The currency the site named with it, empty when it named none.</summary>
        public string TotalCurrency { get; private set; } = string.Empty;

        /// <summary>Records what the site said this group costs, as a number and a currency.</summary>
        public void ShowTotal(decimal amount, string currency, string text)
        {
            TotalAmount = amount;
            TotalCurrency = currency ?? string.Empty;
            TotalText = text;
        }

        /// <summary>The plan this group would send on its own, once priced and read.</summary>
        public UploadPlan? Plan => _plan;

        /// <summary>
        /// Whether this group may go now: assembled, priced, with its files read and its
        /// questions answered.
        /// </summary>
        public bool CanGo =>
            !IsExcluded &&
            _plan is { IsComplete: true } &&
            !_checkingFiles &&
            _files is { CanSend: true } &&
            (!NeedsConfirmation || _confirmationsAccepted);

        /// <summary>The files as the check found them; null until it has run.</summary>
        public OrderFileCheck? Files
        {
            get => _files;
            private set
            {
                if (!SetProperty(ref _files, value))
                    return;

                OnPropertyChanged(nameof(HasFiles));
                OnPropertyChanged(nameof(NeedsConfirmation));
                OnPropertyChanged(nameof(CanGo));
                NotifyProblem();
                OnPropertyChanged(nameof(FileLines));
                OnPropertyChanged(nameof(HasFileLines));

                ApplyCheck();
                _changed();
            }
        }

        /// <summary>Whether the files have been read at all.</summary>
        public bool HasFiles => _files is not null;

        /// <summary>The words that belong to the group rather than to one book.</summary>
        public IReadOnlyList<string> FileLines =>
            _files is null
                ? Array.Empty<string>()
                : _files.OrderAlerts.Select(alert => alert.Message).ToList();

        /// <summary>Whether the group itself has something the books do not.</summary>
        public bool HasFileLines => FileLines.Count > 0;

        /// <summary>Whether the customer has to accept something before this group goes.</summary>
        public bool NeedsConfirmation => _files is { ConfirmationCount: > 0 };

        /// <summary>Whether the customer has said yes to the files this group's site may trim.</summary>
        public bool ConfirmationsAccepted
        {
            get => _confirmationsAccepted;
            set
            {
                if (!SetProperty(ref _confirmationsAccepted, value))
                    return;

                OnPropertyChanged(nameof(CanGo));
                NotifyProblem();
                _changed();
            }
        }

        /// <summary>Whether the files are being read right now.</summary>
        public bool IsCheckingFiles
        {
            get => _checkingFiles;
            private set
            {
                if (!SetProperty(ref _checkingFiles, value))
                    return;

                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(CanGo));
                OnPropertyChanged(nameof(CheckingText));
                NotifyProblem();

                ApplyCheck();
                _changed();
            }
        }

        /// <summary>What the group says about the reading itself.</summary>
        public string CheckingText => _checkingFiles ? "Читаем файлы книги." : string.Empty;

        /// <summary>What the group says about its files once they have been read.</summary>
        public string FilesText
        {
            get => _filesText;
            private set
            {
                if (SetProperty(ref _filesText, value ?? string.Empty))
                    OnPropertyChanged(nameof(HasFilesText));
            }
        }

        /// <summary>Whether there is a line about the files.</summary>
        public bool HasFilesText => _filesText.Length > 0;

        /// <summary>The reading the group has started, so a reader can wait for the answer.</summary>
        public Task FileCheck => _filesRun;

        /// <summary>Reads the questions of the format the group carries.</summary>
        /// <remarks>
        /// A document the site did not open is kept as the group's own sentence: the group stays
        /// in its "reading" state only while the reading is actually running. Before, a failure
        /// left the page saying «читаем параметры формата…» for ever, which read as an error the
        /// customer could do nothing about.
        /// </remarks>
        public async Task LoadParametersAsync()
        {
            _parametersLoaded = false;
            _parametersProblem = null;
            OnPropertyChanged(nameof(ProblemText));

            try
            {
                await _parameters.LoadAsync(Books).ConfigureAwait(true);
            }
            catch (Exception error)
            {
                _parametersProblem = "Параметры формата не открылись: " + error.Message;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(_parameters.Problem))
                    _parametersProblem = _parameters.Problem;

                _parametersLoaded = true;

                OnPropertyChanged(nameof(HasParameters));
                OnPropertyChanged(nameof(ProblemText));
                RefreshReadiness();
            }
        }

        /// <summary>Stops the reading of the files; nothing was sent by it.</summary>
        public void CancelCheck() => _cancelFiles?.Cancel();

        /// <summary>What a new format means for the group: no price, no plan, new questions.</summary>
        private void AfterFormatChosen()
        {
            _parametersLoaded = false;
            _parametersProblem = null;
            ClearOrderProblems();
            RefreshReadiness();
            ForgetPrice();
            ForgetFiles();

            _choiceChanged();
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
                _parametersProblem = "Параметры формата не открылись: " + ex.Message;
                NotifyProblem();
            }
        }

        private void OnFormatChosen(OrderBookRow row)
        {
            // A row written by the group itself is the group's choice being spread over its
            // books, not a new answer: it must not start a second price run per book.
            if (_applyingFormat)
                return;

            _selected = row.SelectedFormat;
            OnPropertyChanged(nameof(SelectedFormat));
            OnPropertyChanged(nameof(SummaryText));

            ApplyFormatToBooks(_selected);
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

        /// <summary>What an answer means for the group: the price on screen is for another set.</summary>
        private void OnAnswerChanged()
        {
            foreach (var row in Books)
                row.ForgetPrice();

            _priceProblem = null;
            _draftNote = null;
            ClearOrderProblems();
            ForgetPrice();
            ForgetFiles();

            _choiceChanged();
            RefreshReadiness();
        }

        private void RefreshReadiness()
        {
            var included = IncludedBooks;

            IsReady = included.Count > 0 &&
                included.All(book => book.IsReady && book.SelectedFormat is not null) &&
                _parametersLoaded &&
                _parameters.IsAnswered;

            OnPropertyChanged(nameof(CanGo));
            OnPropertyChanged(nameof(ProblemText));
            OnPropertyChanged(nameof(SummaryText));

            _changed();

            if (IsReady)
                PriceIfReady();
        }

        /// <summary>Forgets the price and the plan, which belonged to answers that changed.</summary>
        private void ForgetPrice()
        {
            _plan = null;
            ShowTotal(0, string.Empty, string.Empty);

            OnPropertyChanged(nameof(CanGo));
            OnPropertyChanged(nameof(ProblemText));
            _changed();
        }

        /// <summary>Forgets the previous reading, so the group waits for a new one.</summary>
        private void ForgetFiles()
        {
            CancelCheck();
            _cancelFiles = null;

            Files = null;
            FilesText = string.Empty;
            ConfirmationsAccepted = false;

            ApplyCheck();
        }

        /// <summary>Says the group's own words again: why it cannot go, and what that means.</summary>
        private void NotifyProblem()
        {
            OnPropertyChanged(nameof(ProblemText));
            OnPropertyChanged(nameof(NoteText));
            OnPropertyChanged(nameof(HasNote));
            OnPropertyChanged(nameof(NoteIsWarning));
            OnPropertyChanged(nameof(NoteIsProblem));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(HasStatus));
        }

        /// <summary>Forgets the sentences that belonged to the answers that just changed.</summary>
        private void ClearOrderProblems()
        {
            _priceProblem = null;
            _draftNote = null;

            foreach (var row in Books)
                row.ClearProblem();

            NotifyProblem();
        }
    }
}
