using System;
using System.Collections.Generic;
using System.Threading;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Application.Sites.Upload;

namespace PhotoBook.Application.Sites.Books.Files
{
    /// <summary>How far the batch check of one order has got.</summary>
    /// <param name="BooksDone">Books already checked.</param>
    /// <param name="BooksTotal">Books in the order.</param>
    /// <param name="BookNumber">The book being checked, counting from one.</param>
    public sealed record FileCheckProgress(int BooksDone, int BooksTotal, int BookNumber);

    /// <summary>One book of the order as the check found it.</summary>
    /// <param name="Number">The book's place in the order, counting from one.</param>
    /// <param name="Name">What the window calls the book.</param>
    /// <param name="Rule">What a page of this book has to be, or null when the site did not say.</param>
    /// <param name="Alerts">What is wrong with its files, in page order.</param>
    public sealed record CheckedBook(
        int Number,
        string Name,
        PageImageRule? Rule,
        IReadOnlyList<FileAlert> Alerts);

    /// <summary>What the check found in a whole order, and whether it can go.</summary>
    /// <remarks>
    /// The verdict sits next to the alerts rather than being worked out by each reader: whether
    /// an order may be sent is one rule, and a window that decided it on its own would be a
    /// second place for the same rule to be decided differently.
    /// </remarks>
    public sealed record OrderFileCheck(IReadOnlyList<CheckedBook> Books, IReadOnlyList<FileAlert> OrderAlerts)
    {
        /// <summary>Problems that stop the order outright.</summary>
        public int BlockingCount => CountOf(FileAlertLevel.Blocking);

        /// <summary>Problems the customer has to say yes to before the order goes.</summary>
        public int ConfirmationCount => CountOf(FileAlertLevel.NeedsConfirmation);

        /// <summary>Whether nothing stops the order from being sent.</summary>
        public bool CanSend => Books.Count > 0 && BlockingCount == 0;

        private int CountOf(FileAlertLevel level)
        {
            var count = 0;

            for (var index = 0; index < OrderAlerts.Count; index++)
            {
                if (OrderAlerts[index].Level == level)
                    count++;
            }

            for (var book = 0; book < Books.Count; book++)
            {
                var alerts = Books[book].Alerts;
                for (var index = 0; index < alerts.Count; index++)
                {
                    if (alerts[index].Level == level)
                        count++;
                }
            }

            return count;
        }
    }

    /// <summary>Checks every book of an upload plan against what the site asks of a page.</summary>
    /// <remarks>
    /// <para>
    /// The plan is the unit, not the book: it is exactly what the upload would send, so a book
    /// the plan left out is not checked and an order that cannot go at all has nothing to check.
    /// Every book of a project is checked, not just the first: a project holds several books and
    /// the site refuses the whole project over one bad file in any of them. The books are read
    /// one after another, each announcing progress, and the whole run stops between books and
    /// between pages when the customer cancels.
    /// </para>
    /// <para>
    /// A format whose geometry cannot be read is a blocking alert on that book, not a failed
    /// check: the customer needs to know which book cannot go, and asking one question fewer
    /// about the rest is not an improvement.
    /// </para>
    /// </remarks>
    public sealed class OrderFileCheckService
    {
        private readonly ISiteCatalogService _catalog;
        private readonly IBookFileInspector _inspector;

        public OrderFileCheckService(ISiteCatalogService catalog, IBookFileInspector inspector)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        }

        /// <summary>Checks the groups an upload would send, one at a time.</summary>
        public async System.Threading.Tasks.Task<OrderFileCheck> CheckAsync(
            IReadOnlyList<UploadGroup> groups,
            IProgress<FileCheckProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (groups is null)
                throw new ArgumentNullException(nameof(groups));

            var total = 0;
            for (var index = 0; index < groups.Count; index++)
                total += groups[index].Group.Books.Count;

            var books = new List<CheckedBook>(total);

            foreach (var group in groups)
            {
                foreach (var book in group.Group.Books)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var number = books.Count + 1;
                    progress?.Report(new FileCheckProgress(books.Count, total, number));
                    books.Add(await CheckBookAsync(group, book, number, cancellationToken)
                        .ConfigureAwait(false));
                }
            }

            if (total > 0)
                progress?.Report(new FileCheckProgress(books.Count, total, total));

            return new OrderFileCheck(books, Array.Empty<FileAlert>());
        }

        private async System.Threading.Tasks.Task<CheckedBook> CheckBookAsync(
            UploadGroup group,
            Book book,
            int number,
            CancellationToken cancellationToken)
        {
            var name = NameOf(book, number);

            var layout = await _catalog
                .GetLayoutAsync(
                    group.Group.Region,
                    group.Request.ProductId,
                    group.Request.ProductComponentId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!layout.IsSuccess || layout.Value is null)
                return Refused(number, name, ": сайт не назвал размер страницы, проверить файлы нечем.");

            var page = layout.Value.Part(book.PagePartId);
            var required = page is null
                ? null
                : layout.Value.RequiredPixels(book.PagePartId, book.PageSlots, book.PagePart?.Selection.Elements);

            if (page is null || required is null)
                return Refused(number, name, ": сайт не описал страницу этого формата, проверить файлы нечем.");

            var rule = PageImageRule.For(required, page.ErrorMm, layout.Value.ResolutionDpi);

            var alerts = new List<FileAlert>();
            alerts.AddRange(await CheckCoverAsync(book, name, layout.Value, cancellationToken)
                .ConfigureAwait(false));

            // The plan sends the cover first; the page rule is for the spreads only, so the
            // inspection names them rather than taking whatever the group holds.
            var spreadFiles = book.Files ?? (IReadOnlyList<PageFile>)Array.Empty<PageFile>();
            var facts = await _inspector
                .InspectAsync(spreadFiles, cancellationToken)
                .ConfigureAwait(false);

            alerts.AddRange(BookFileCheck.Check(facts, rule));

            return new CheckedBook(number, name, rule, alerts);
        }

        /// <summary>The cover's own size, checked against the cover part and not the spread.</summary>
        /// <remarks>
        /// The two are different files of different sizes, and the site says so itself: the cover
        /// is two spreads plus the spine, and a cover checked against a spread's pixels would be
        /// refused for being right.
        /// </remarks>
        private async System.Threading.Tasks.Task<IReadOnlyList<FileAlert>> CheckCoverAsync(
            Books.Book book,
            string name,
            Catalog.Layout.SiteLayout layout,
            CancellationToken cancellationToken)
        {
            if (!book.NeedsCover || book.CoverFile is not { } cover)
                return Array.Empty<FileAlert>();

            var coverPart = layout.Part(book.CoverPartId);
            var required = coverPart is null
                ? null
                : layout.RequiredPixels(book.CoverPartId, 1, book.CoverPart?.Selection.Elements);

            if (required is null)
            {
                return new[]
                {
                    new FileAlert(
                        FileAlertLevel.Blocking,
                        null,
                        name + ": сайт не описал обложку этого формата, проверить её размер нечем."),
                };
            }

            var rule = PageImageRule.For(required, coverPart!.ErrorMm, layout.ResolutionDpi);
            var facts = await _inspector
                .InspectAsync(new[] { cover }, cancellationToken)
                .ConfigureAwait(false);

            return BookFileCheck.Check(facts, rule, "Обложка");
        }

        private static CheckedBook Refused(int number, string name, string reason) =>
            new(number, name, null, new[] { new FileAlert(FileAlertLevel.Blocking, null, name + reason) });

        /// <summary>The book as the window names it: its own name, else its number.</summary>
        private static string NameOf(Books.Book book, int number) =>
            string.IsNullOrWhiteSpace(book.Product?.Title) || string.IsNullOrWhiteSpace(book.Variant?.Title)
                ? "Книга " + number
                : "Книга " + number + " («" + book.Product.Title + " " + book.Variant.Title + "»)";
    }
}
