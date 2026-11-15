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

        /// <summary>The plan for an order, with every book that cannot go reported by number.</summary>
        public static UploadPlan Build(
            SiteRegion region,
            OrderDraft? draft,
            GroupQuote? quote,
            IReadOnlyList<SiteProduct>? formats)
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
                        "Группа «" + first.Variant.Title + "»: " + books.Count + " книг — это больше " +
                        MaxFilesPerProject + " файлов, больше одного проекта сайт не примет. " +
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
                        new CreateProjectRequest(first.Product.Id, first.Variant.Id, integration),
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

            foreach (var book in books)
            {
                var count = book.AllFiles.Count;

                if (current.Count > 0 && files + count > MaxFilesPerProject)
                {
                    parts.Add(current);
                    current = new List<Book>();
                    files = 0;
                }

                current.Add(book);
                files += count;
            }

            if (current.Count > 0)
                parts.Add(current);

            return parts;
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
