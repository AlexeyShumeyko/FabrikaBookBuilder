using System;
using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Grouping;
using PhotoBook.Application.Sites.Books.Order;
using PhotoBook.Application.Sites.Books.Pricing;
using PhotoBook.Application.Sites.Books.Submission;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Application.Sites.Projects;

namespace PhotoBook.Application.Sites.Upload
{
    /// <summary>Everything the upload screen needs, assembled from answers already in hand.</summary>
    /// <param name="Summary">The order as the customer reviews it, over the groups that can go.</param>
    /// <param name="Groups">The groups to send, one project each.</param>
    /// <param name="Problems">Books left out, and why; any entry means nothing may be sent.</param>
    /// <param name="Notes">
    /// What the customer should know but need not fix, such as a group split over several
    /// projects because one project takes no more than 500 files.
    /// </param>
    public sealed record UploadPlan(
        SubmissionSummary Summary,
        IReadOnlyList<UploadGroup> Groups,
        IReadOnlyList<string> Problems,
        IReadOnlyList<string>? Notes = null)
    {
        /// <summary>Whether the upload may start at all.</summary>
        public bool IsComplete => Problems.Count == 0 && Summary.CanUpload && Groups.Count > 0;
    }

    /// <summary>
    /// The order turned into what the upload sends: one project per format, with the site's ids
    /// and the prices already obtained.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The plan is pure: the books, their prices and the format documents are answers the order
    /// page already holds, so opening the upload screen asks the site nothing. Nothing here is
    /// written either - the screen is still the only place that starts a write.
    /// </para>
    /// <para>
    /// One project per format, and every book of that format travels in it: the site's project
    /// holds several books, each with its own cover and spreads. The price of such a project is
    /// the sum of its books, because the site stores what it is sent and does not multiply by the
    /// number of books. A book whose format has no integration, or that the site did not price,
    /// is left out by name rather than guessed at.
    /// </para>
    /// </remarks>
    public static class UploadPlans
    {
        /// <summary>Files one project on the site takes; the site refuses the rest.</summary>
        public const int MaxFilesPerProject = 500;

        /// <summary>
        /// Books one project on the site takes. The uploader's settings name fifty
        /// (<c>common.starting.projectItemsLimit</c>), and it never places a book past that
        /// position: a project asked for a fifty-first book answers without one.
        /// </summary>
        public const int MaxBooksPerProject = 50;

        /// <summary>
        /// Bytes one project on the site takes. The uploader's own settings name 1 GiB, and a
        /// run that reaches it starts answering 5xx halfway through a book - which reads as
        /// "the site is unavailable" but is really a project that is too large.
        /// </summary>
        public const long MaxBytesPerProject = 1024L * 1024 * 1024;

        /// <summary>The plan for an order, with every book that cannot go reported by number.</summary>
        /// <param name="projectName">The name the customer gave the project; the site names each created project with it.</param>
        public static UploadPlan Build(
            SiteRegion region,
            OrderDraft? draft,
            GroupQuote? quote,
            IReadOnlyList<SiteProduct>? formats,
            string? projectName = null)
        {
            if (region is null)
                throw new ArgumentNullException(nameof(region));

            if (draft is null)
                return Refused("Заказ не собран.");

            var problems = new List<string>(draft.Problems);
            // The format is what the site's project is made of, so the layout follows it: books of
            // one format share a project, books of another get one of their own.
            var layout = BookGrouping.Split(region, draft.Books, GroupingMode.InOneProject);

            var groups = new List<UploadGroup>(layout.Groups.Count);
            var sent = new List<OrderGroup>(layout.Groups.Count);
            var quotes = new List<GroupQuote>(layout.Groups.Count);
            var notes = new List<string>();
            var number = 0;
            var bookNumber = 0;

            foreach (var group in layout.Groups)
            {
                var books = group.Books;
                var first = books[0];

                var integration = IntegrationOf(first, formats);
                if (integration <= 0)
                {
                    problems.Add(GroupName(books, number + 1, bookNumber + 1) +
                        ": сайт не назвал интеграцию загрузчика для этого формата.");
                    continue;
                }

                var quoted = new List<BookQuote>(books.Count);

                foreach (var book in books)
                {
                    bookNumber++;

                    var priced = quote?.Books.FirstOrDefault(item => ReferenceEquals(item.Book, book));
                    if (priced is null)
                    {
                        problems.Add(NameOf(book, bookNumber) + ": цена не получена.");
                        continue;
                    }

                    quoted.Add(priced);
                }

                // One book without a price stops its whole project: a project is priced as one,
                // and sending the rest would create an order the customer never saw a price for.
                if (quoted.Count != books.Count)
                    continue;

                var prices = new Dictionary<Book, BookQuote>(ReferenceEqualityComparer.Instance);
                foreach (var item in quoted)
                    prices[item.Book] = item;
                var parts = Parts(books);

                if (parts.Count > 1)
                {
                    notes.Add(
                        "Группа «" + first.Variant.Title + "»: " + books.Count + " книг — это " +
                        TooLargeForOneProject(books) + ", больше одного проекта сайт не примет. " +
                        "Отправляем " + parts.Count + " проекта.");
                }

                foreach (var part in parts)
                {
                    number++;

                    var partQuote = new GroupQuote(part.Select(book => prices[book]).ToList());
                    var partGroup = new OrderGroup(region, part);

                    groups.Add(new UploadGroup(
                        number,
                        partGroup,
                        new CreateProjectRequest(first.Product.Id, first.Variant.Id, integration, projectName ?? string.Empty),
                        part.SelectMany(book => book.AllFiles).ToList(),
                        partQuote.Total,
                        partQuote.Currency));

                    sent.Add(partGroup);
                    quotes.Add(partQuote);
                }
            }

            if (groups.Count == 0)
            {
                if (problems.Count == 0)
                    problems.Add("В заказе нет ни одной книги.");

                // Nothing goes, so the summary covers no projects at all: pairing it with the
                // layout would claim groups that were left out, and the screen would show a
                // price for them.
                var nothing = new BookGroups(Array.Empty<OrderGroup>(), layout.Problems);

                return new UploadPlan(
                    SubmissionSummary.Build(nothing, Array.Empty<GroupQuote>()), groups, problems, notes);
            }

            // The summary covers the groups that go, so its own blockers are about them; the
            // books left out are named in Problems, and one of those stops the run.
            var going = new BookGroups(sent, layout.Problems);

            return new UploadPlan(SubmissionSummary.Build(going, quotes), groups, problems, notes);
        }

        /// <summary>
        /// One plan over plans the page built group by group, in the order they go.
        /// </summary>
        /// <remarks>
        /// The page prices each group of books apart, because a group is one project on the site
        /// and only the page knows which groups are ready. What the upload screen receives is the
        /// whole run: the groups of every plan, numbered as one order, with the notes and the
        /// problems of all of them. Returns null when there is nothing to send at all, so a page
        /// that left every group out offers no plan rather than an empty run.
        /// </remarks>
        public static UploadPlan? Combine(IReadOnlyList<UploadPlan>? plans, IReadOnlyList<string>? notes = null)
        {
            var parts = (plans ?? Array.Empty<UploadPlan>())
                .Where(plan => plan is not null && plan.Groups.Count > 0)
                .ToList();

            if (parts.Count == 0)
                return null;

            var groups = new List<UploadGroup>();
            foreach (var plan in parts)
            {
                foreach (var group in plan.Groups)
                    groups.Add(group with { Number = groups.Count + 1 });
            }

            var lines = parts.SelectMany(plan => plan.Notes ?? Array.Empty<string>()).ToList();
            if (notes is not null)
                lines.AddRange(notes);

            var problems = parts.SelectMany(plan => plan.Problems).ToList();
            var summary = SubmissionSummary.Combine(parts.Select(plan => plan.Summary).ToList());

            return new UploadPlan(summary, groups, problems, lines);
        }

        /// <summary>
        /// The books of one format split into as few projects as the site's file limit allows.
        /// </summary>
        /// <remarks>
        /// A book is never split: its cover and spreads are positions of one book on the site.
        /// The parts are filled in the order the books came in, so the first project holds the
        /// first books and the customer can find them on the site by their order.
        /// </remarks>
        private static List<List<Book>> Parts(IReadOnlyList<Book> books)
        {
            var parts = new List<List<Book>>();
            var current = new List<Book>();
            var files = 0;
            var bytes = 0L;

            foreach (var book in books)
            {
                var count = book.AllFiles.Count;
                var size = book.AllFiles.Sum(file => file.LengthBytes);

                // Every limit belongs to one project: the site stops taking files after five
                // hundred, its uploader's settings name a gigabyte and fifty books. A book is
                // never split, so a single book over a limit travels alone.
                if (current.Count > 0 &&
                    (current.Count >= MaxBooksPerProject ||
                     files + count > MaxFilesPerProject ||
                     bytes + size > MaxBytesPerProject))
                {
                    parts.Add(current);
                    current = new List<Book>();
                    files = 0;
                    bytes = 0;
                }

                current.Add(book);
                files += count;
                bytes += size;
            }

            if (current.Count > 0)
                parts.Add(current);

            return parts;
        }

        /// <summary>Which of the site's two limits the group passes, in the note's own words.</summary>
        private static string TooLargeForOneProject(IReadOnlyList<Book> books)
        {
            if (books.Count > MaxBooksPerProject)
                return "больше " + MaxBooksPerProject + " книг";

            var files = books.Sum(book => book.AllFiles.Count);
            if (files > MaxFilesPerProject)
                return "больше " + MaxFilesPerProject + " файлов";

            var bytes = books.Sum(book => book.AllFiles.Sum(file => file.LengthBytes));
            return "больше " + (bytes / (1024L * 1024 * 1024)) + " ГиБ";
        }

        /// <summary>The uploader integration of the book's format, or zero when it is not known.</summary>
        private static long IntegrationOf(Book book, IReadOnlyList<SiteProduct>? formats) =>
            formats?
                .Where(format => format.Id == book.Product.Id)
                .Where(format => format.Variants is not null &&
                                 format.Variants.Any(variant => variant.Id == book.Variant.Id))
                .Select(format => format.IntegrationId)
                .FirstOrDefault() ?? 0;

        /// <summary>The group as a message names it: one book by its name, several by format.</summary>
        private static string GroupName(IReadOnlyList<Book> books, int number, int bookNumber) =>
            books.Count == 1
                ? NameOf(books[0], bookNumber)
                : "Группа " + number + " («" + books[0].Product.Title + " " + books[0].Variant.Title +
                  "», книг: " + books.Count + ")";

        /// <summary>The book as a message names it: its own name, else its number.</summary>
        private static string NameOf(Book book, int number) =>
            string.IsNullOrWhiteSpace(book.Product?.Title) || string.IsNullOrWhiteSpace(book.Variant?.Title)
                ? "Книга " + number
                : "Книга " + number + " («" + book.Product.Title + " " + book.Variant.Title + "»)";

        private static UploadPlan Refused(string problem) =>
            new(SubmissionSummary.Build(new BookGroups(Array.Empty<OrderGroup>(), new[] { problem }), Array.Empty<GroupQuote>()),
                Array.Empty<UploadGroup>(),
                new[] { problem });
    }
}
