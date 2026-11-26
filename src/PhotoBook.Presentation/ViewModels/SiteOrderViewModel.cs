using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Files;
using PhotoBook.Application.Sites.Books.Grouping;
using PhotoBook.Application.Sites.Books.Order;
using PhotoBook.Application.Sites.Books.Pricing;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Application.Sites.Projects;
using PhotoBook.Application.Sites.Upload;
using CoreProject = PhotoBook.Core.Project;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>
    /// The order page: the project's books, laid out into the groups that would become projects
    /// on the site - each with its format, its parameters, its price - and the way on.
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
    /// <b>Groups.</b> A project on the site holds one format, while a program project may hold
    /// books of two - two folders assembled side by side, exported at two page sizes. Those
    /// cannot go as one order, so the page lays them out as separate groups before anything is
    /// chosen, and every group answers for itself. One broken group must not take the others
    /// down: what the page sends is the plans of the groups that can go, and the rest are named
    /// in a warning rather than silently dropped.
    /// </para>
    /// </remarks>
    public sealed partial class SiteOrderViewModel : ViewModelBase
    {
        private readonly ISiteCatalogService? _catalog;
        private readonly ISiteSessionStore? _sessions;
        private readonly OrderPriceService? _prices;
        private readonly OrderFileCheckService? _fileCheck;
        private readonly IUploadedProjectStore? _uploaded;
        private readonly SiteOrderUpload _upload;

        private IReadOnlyList<OrderBookRow> _books = Array.Empty<OrderBookRow>();
        private IReadOnlyList<SiteOrderGroup> _groups = Array.Empty<SiteOrderGroup>();
        private IReadOnlyList<FormatChoice> _formats = Array.Empty<FormatChoice>();

        private string _projectName = string.Empty;
        private int _spreadCount;
        private bool _isReady;
        private bool _isLoadingFormats;

        public SiteOrderViewModel(
            ISiteCatalogService? catalog = null,
            ISiteSessionStore? sessions = null,
            IGroupQuoteService? quotes = null,
            IFileService? files = null,
            OrderFileCheckService? fileCheck = null,
            IUploadedProjectStore? uploaded = null)
        {
            _catalog = catalog;
            _sessions = sessions;
            _fileCheck = fileCheck;
            _uploaded = uploaded;

            // The price is the order service's job; the page only carries its answer. The two
            // ports are given together because one without the other can price nothing.
            _prices = quotes is not null && files is not null
                ? new OrderPriceService(files, quotes)
                : null;

            _upload = new SiteOrderUpload(() =>
            {
                OnPropertyChanged(nameof(CanUpload));
                OnPropertyChanged(nameof(TotalText));
                OnPropertyChanged(nameof(HasTotal));
            });

            _upload.Requested += (_, _) => UploadRequested?.Invoke(this, EventArgs.Empty);
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

        /// <summary>The project's books, in the project's order.</summary>
        public IReadOnlyList<OrderBookRow> Books
        {
            get => _books;
            private set
            {
                if (SetProperty(ref _books, value))
                    OnPropertyChanged(nameof(BookCountText));
            }
        }

        /// <summary>
        /// The groups the order was laid out into, in the order they go.
        /// </summary>
        /// <remarks>
        /// One group per format, decided from the books' own page size: the format stored on a
        /// book cannot tell them apart, because a project assembled from two folders carries one
        /// stored format on both books.
        /// </remarks>
        public IReadOnlyList<SiteOrderGroup> Groups
        {
            get => _groups;
            private set
            {
                if (SetProperty(ref _groups, value))
                    OnPropertyChanged(nameof(HasGroups));
            }
        }

        /// <summary>Whether the order has anything to lay out.</summary>
        public bool HasGroups => _groups.Count > 0;

        /// <summary>The formats the site sells, in the site's order.</summary>
        public IReadOnlyList<FormatChoice> Formats => _formats;

        /// <summary>Whether the site named any format at all.</summary>
        public bool HasFormats => _formats.Count > 0;

        /// <summary>How many books are going, across the groups.</summary>
        public string BookCountText => Books.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);

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

        /// <summary>Whether every group is full, has a format and its parameters answered.</summary>
        public bool IsReady
        {
            get => _isReady;
            private set => SetProperty(ref _isReady, value);
        }

        /// <summary>Whether the catalog is being read right now.</summary>
        public bool IsLoadingFormats
        {
            get => _isLoadingFormats;
            private set
            {
                if (!SetProperty(ref _isLoadingFormats, value))
                    return;

                OnPropertyChanged(nameof(CanUpload));
            }
        }

        /// <summary>
        /// The order's total as the site writes prices: the sum of the groups it has a price for.
        /// </summary>
        /// <remarks>
        /// Summed from the groups rather than read off the plan, because the plan is what will be
        /// sent and the total is what the site said: a group whose run is still being prepared is
        /// still a group with a price the customer has seen.
        /// </remarks>
        public string TotalText => HasTotal
            ? _total.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) +
              (_currency.Length == 0 ? string.Empty : " " + _currency)
            : string.Empty;

        /// <summary>Whether the site has named a total.</summary>
        public bool HasTotal => Groups.Any(group => group.HasTotal);

        /// <summary>
        /// Whether the order can be sent: at least one group is ready, priced and read, and no
        /// group is still waiting for an answer. A group that cannot go does not hold the others
        /// back - it is named in the warning beside the button instead.
        /// </summary>
        public bool CanUpload =>
            !IsLoadingFormats &&
            !Groups.Any(group => group.IsBusy) &&
            _upload.CanUpload;

        /// <summary>The price each group is asking for, so a reader can wait for the answers.</summary>
        public Task Pricing =>
            Groups.Count == 0
                ? Task.CompletedTask
                : Task.WhenAll(Groups.Select(group => group.Pricing));

        /// <summary>The reading of the files of every group, so a reader can wait for it.</summary>
        public Task FileCheck =>
            Groups.Count == 0
                ? Task.CompletedTask
                : Task.WhenAll(Groups.Select(group => group.FileCheck));

        /// <summary>
        /// Reads the project into the page. A project that is not assembled comes back with the
        /// books still listed and the reason why nothing can be sent.
        /// </summary>
        public void Prepare(CoreProject? project, string? projectName = null)
        {
            _formats = Array.Empty<FormatChoice>();
            _upload.Offer(null);

            if (project is null)
            {
                Books = Array.Empty<OrderBookRow>();
                Groups = Array.Empty<SiteOrderGroup>();
                ProjectName = string.Empty;
                SpreadCount = 0;
                IsReady = false;
                SetProjectMessage("Проект не открыт.");
                NotifyCard();
                return;
            }

            var inventory = BookInventory.From(project);

            // One row per project book, numbered as the project numbers them: the rows stand in
            // the groups, but a number is the book's place in the project and does not change
            // because the order was laid out into two of them.
            var rows = project.Books
                .Select((book, index) => new OrderBookRow(
                    book,
                    index + 1,
                    () => _formats,

                    // No picker of its own: the format is chosen once for the group and written
                    // into its books, so a row never answers for the group.
                    _ => { },
                    OnBookExclusionChanged))
                .ToList();

            Books = rows;

            var byBook = rows.ToDictionary(row => row.Book, row => row);

            // The project's own name travels into every project the run creates, so the account
            // shows the name the customer knows rather than an empty title.
            var name = string.IsNullOrWhiteSpace(projectName) ? "Проект" : projectName!.Trim();

            var groups = new List<SiteOrderGroup>();
            foreach (var set in BookSplitting.Split(project.Books))
            {
                var groupRows = set.Where(book => byBook.ContainsKey(book))
                    .Select(book => byBook[book])
                    .ToList();

                if (groupRows.Count == 0)
                    continue;

                groups.Add(new SiteOrderGroup(
                    groups.Count + 1,
                    groupRows,
                    _catalog,
                    _sessions,
                    _prices,
                    _fileCheck,
                    OnGroupChanged,
                    OnChoiceChanged,
                    _uploaded,
                    name));
            }

            Groups = groups;

            // The prototype opens with one card chosen, and so does the page: the first group
            // is the one the eye meets, and a choice already made says what the gold means.
            SelectedGroup = groups.FirstOrDefault();

            ProjectName = name;
            SpreadCount = inventory.Books.Sum(book => book.Spreads);

            // The project's own sentences are no longer collected here: a book that is not
            // filled says so in its row, and the group names it. A card over the page repeated
            // them and made the customer look for the book it meant.
            SetProjectMessage(string.Empty);

            RefreshReadiness();
        }

        /// <summary>
        /// Reads the formats the site offers and the parameters of the ones already chosen.
        /// </summary>
        /// <remarks>
        /// One read for the page rather than one per group: the list is the same for all of them,
        /// and asking per group would multiply the same request by the number of projects the
        /// order becomes. The parameters come from the uploader's document for each chosen
        /// format, kept by the catalog service, so a second visit to the page asks the site
        /// nothing.
        /// </remarks>
        public async Task LoadFormatsAsync()
        {
            if (_catalog is null || _sessions is null)
                return;

            var session = _sessions.Load();
            if (session is null)
            {
                SetFormatNote("Выбор формата появится после входа на сайт.");
                return;
            }

            IsLoadingFormats = true;
            try
            {
                var result = await _catalog.GetProductsAsync(session.Region).ConfigureAwait(true);

                if (!result.IsSuccess || result.Value is null)
                {
                    SetFormatNote(result.Error ?? "Каталог сайта не открылся.");
                    return;
                }

                ApplyFormats(result.Value);

                // One group after another, not in parallel: the platform is shared, and the page
                // is asking it the same question per group.
                foreach (var group in Groups)
                    await group.LoadParametersAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SetFormatNote("Каталог сайта не открылся: " + ex.Message);
            }
            finally
            {
                IsLoadingFormats = false;
            }
        }

        /// <summary>Says something on the page that came from outside it, such as a failed save.</summary>
        public void ShowMessage(string text) => Note(text);

        /// <summary>Goes back to the project the page was opened from.</summary>
        public void Back() => BackRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// Hands the order to the shell, which opens the upload screen. Nothing is sent until
        /// that screen's own button is pressed.
        /// </summary>
        /// <remarks>
        /// The plan is rebuilt here rather than trusted from the last change: the groups that can
        /// go are the groups as they stand at the moment of the press, and a run assembled from a
        /// plan that is a second old would send a group the customer has just broken.
        /// </remarks>
        public void Upload()
        {
            RebuildPlan();
            _upload.Request();
        }

        private void ApplyFormats(IReadOnlyList<CatalogProduct> products)
        {
            _formats = products
                .SelectMany(product => product.Formats.Select(format => new FormatChoice(product, format)))
                .ToList();

            OnPropertyChanged(nameof(Formats));
            OnPropertyChanged(nameof(HasFormats));

            foreach (var group in Groups)
                group.ApplyFormats(_formats);

            SetFormatNote(_formats.Count == 0 ? "Сайт не предложил ни одного формата." : string.Empty);
            RefreshReadiness();
        }

        /// <summary>Re-reads whether the order as a whole is answerable, and rebuilds the plan.</summary>
        private void RefreshReadiness()
        {
            IsReady = Groups.Count > 0 && Groups.All(group => group.IsReady);

            RebuildPlan();
        }

        /// <summary>
        /// Builds what would be sent from the groups that can go, and says what stays behind.
        /// </summary>
        /// <remarks>
        /// A group with a broken file or an unanswered question is left out rather than taking
        /// the ready groups down with it: the customer assembled several orders in one project,
        /// and sending the good ones is what they asked for. It is left out loudly - the warning
        /// beside the button names the group and says its books will not be uploaded - because a
        /// run that quietly skipped a book would leave the customer with a project on the site
        /// that is missing a part they had assembled.
        /// </remarks>
        private void RebuildPlan()
        {
            var going = new List<SiteOrderGroup>();
            var left = new List<SiteOrderGroup>();

            foreach (var group in Groups.Where(group => !group.IsExcluded))
                (group.CanGo ? going : left).Add(group);

            // The total covers what the site priced: a group whose price is still being asked for
            // is not a zero, it is a group without a number yet.
            var priced = Groups.Where(group => group.HasTotal).ToList();

            _total = priced.Sum(group => group.TotalAmount);
            _currency = priced
                .Select(group => group.TotalCurrency)
                .FirstOrDefault(currency => currency.Length > 0) ?? string.Empty;

            // A group's card says whether its books stay behind, and says it as a warning only
            // when something else is going: with every group stuck, a line about books left
            // behind would describe a send that cannot happen.
            foreach (var group in Groups)
                group.MarkStaying(going.Count > 0 && left.Contains(group));

            var notes = going.Count > 0
                ? left.Select(SkippedLine).ToList()
                : new List<string>();

            var plan = going.Count == 0
                ? null
                : UploadPlans.Combine(going.Select(group => group.Plan!).ToList(), notes);

            _upload.Offer(plan);

            OnPropertyChanged(nameof(TotalText));
            OnPropertyChanged(nameof(HasTotal));
            OnPropertyChanged(nameof(CanUpload));

            // The head is rebuilt from the same answer as the plan, so it can never say a
            // price or a count that belongs to the order as it stood a moment ago.
            NotifyCard();
        }

        /// <summary>The line that names a group left out, and what will happen to its books.</summary>
        private static string SkippedLine(SiteOrderGroup group)
        {
            var reason = group.ProblemText.Trim();
            if (reason.Length == 0)
                reason = "группа не готова.";

            return "Группа " + group.Number + ": " + reason +
                " Книги этой группы загружены не будут.";
        }

        private void OnGroupChanged()
        {
            // A group becomes answerable on its own schedule - an answer, a price, a reading of
            // its files - so the page's own word about the order is re-read here rather than only
            // when the page does something.
            IsReady = Groups.Count > 0 && Groups.All(group => group.IsReady);

            OnPropertyChanged(nameof(IsReady));
            OnPropertyChanged(nameof(CanUpload));
            OnPropertyChanged(nameof(TotalText));
            OnPropertyChanged(nameof(HasTotal));

            RebuildPlan();
        }

        private void OnChoiceChanged() => ChoiceChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>Re-reads a group after one of its books was taken out of the run.</summary>
        private void OnBookExclusionChanged(OrderBookRow row)
        {
            GroupOf(row)?.BookExclusionChanged();
            OnPropertyChanged(nameof(CanUpload));
        }
    }
}
