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
    /// <param name="Groups">The groups to send, one book each.</param>
    /// <param name="Problems">Books left out, and why; any entry means nothing may be sent.</param>
    public sealed record UploadPlan(
        SubmissionSummary Summary,
        IReadOnlyList<UploadGroup> Groups,
        IReadOnlyList<string> Problems)
    {
        /// <summary>Whether the upload may start at all.</summary>
        public bool IsComplete => Problems.Count == 0 && Summary.CanUpload && Groups.Count > 0;
    }

    /// <summary>
    /// The order turned into what the upload sends: one project per book, with the site's ids
    /// and the prices already obtained.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The plan is pure: the books, their prices and the format documents are answers the order
    /// page already holds, so opening the upload screen asks the site nothing. Nothing here is
    /// written either - the screen is still the only place that starts a write.
    /// </para>
    /// <para>
    /// One book per project, because the site's project holds one item: laying several books into
    /// one project would put the second book's pages into the first. A book whose format has no
    /// integration, or that the site did not price, is left out by name rather than guessed at.
    /// </para>
    /// </remarks>
    public static class UploadPlans
    {
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
            var layout = BookGrouping.Split(region, draft.Books, GroupingMode.EachBookSeparate);

            var groups = new List<UploadGroup>(layout.Groups.Count);
            var sent = new List<OrderGroup>(layout.Groups.Count);
            var quotes = new List<GroupQuote>(layout.Groups.Count);
            var number = 0;

            foreach (var group in layout.Groups)
            {
                number++;

                if (group.Books.Count != 1)
                {
                    problems.Add("Группа " + number + ": сайт принимает одну книгу на проект.");
                    continue;
                }

                var book = group.Books[0];
                var name = NameOf(book, number);

                var integration = IntegrationOf(book, formats);
                if (integration <= 0)
                {
                    problems.Add(name + ": сайт не назвал интеграцию загрузчика для этого формата.");
                    continue;
                }

                var priced = quote?.Books.FirstOrDefault(item => ReferenceEquals(item.Book, book));
                if (priced is null)
                {
                    problems.Add(name + ": цена не получена.");
                    continue;
                }

                groups.Add(new UploadGroup(
                    number,
                    group,
                    new CreateProjectRequest(book.Product.Id, book.Variant.Id, integration),
                    book.AllFiles,
                    priced.Check.Quote?.Amount ?? 0));

                sent.Add(group);
                quotes.Add(new GroupQuote(new[] { priced }));
            }

            if (groups.Count == 0)
            {
                if (problems.Count == 0)
                    problems.Add("В заказе нет ни одной книги.");

                return new UploadPlan(SubmissionSummary.Build(layout, Array.Empty<GroupQuote>()), groups, problems);
            }

            // The summary covers the groups that go, so its own blockers are about them; the
            // books left out are named in Problems, and one of those stops the run.
            var going = new BookGroups(sent, layout.Problems);

            return new UploadPlan(SubmissionSummary.Build(going, quotes), groups, problems);
        }

        /// <summary>The uploader integration of the book's format, or zero when it is not known.</summary>
        private static long IntegrationOf(Book book, IReadOnlyList<SiteProduct>? formats) =>
            formats?
                .Where(format => format.Id == book.Product.Id)
                .Where(format => format.Variants is not null &&
                                 format.Variants.Any(variant => variant.Id == book.Variant.Id))
                .Select(format => format.IntegrationId)
                .FirstOrDefault() ?? 0;

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
