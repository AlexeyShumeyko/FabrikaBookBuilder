using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites.Books.Pricing;
using PhotoBook.Application.Sites.Catalog;
using CoreBook = PhotoBook.Core.Book;
using CoreProject = PhotoBook.Core.Project;

namespace PhotoBook.Application.Sites.Books.Order
{
    /// <summary>What the site said about an assembled order.</summary>
    /// <param name="Quote">The price of every book, when the order could be assembled at all.</param>
    /// <param name="Problems">What stops the order, or why some book has no price.</param>
    /// <param name="Draft">
    /// The books as they were priced. The upload sends exactly these, so the screen that sends
    /// does not assemble the order a second time and cannot send something the price was not
    /// asked for.
    /// </param>
    public sealed record PricedOrder(
        GroupQuote? Quote,
        IReadOnlyList<string> Problems,
        OrderDraft? Draft = null)
    {
        /// <summary>Whether every book of the order has a price.</summary>
        public bool IsPriced => Quote is { IsPriced: true };

        /// <summary>The total as the site wrote it, or an empty string when it named none.</summary>
        public string TotalText => Quote?.TotalText ?? string.Empty;

        /// <summary>An order that never reached the site's price list, with the reason.</summary>
        /// <remarks>
        /// The draft travels with the refusal: the page needs each refused book's own row to
        /// show the complaint on the book rather than in a sentence over the whole order.
        /// </remarks>
        public static PricedOrder Refused(OrderDraft draft) =>
            new(null, draft.Problems, draft);
    }

    /// <summary>
    /// Prices an order the way it would be sent.
    /// </summary>
    /// <remarks>
    /// The price is the price of the order itself, not of the page's idea of it: the same
    /// assembly the upload uses builds the request, so a book that would be refused at upload is
    /// refused here, by name, instead of being priced and then failing later. The books are
    /// quoted one after another, and the site is asked nothing for a book that could not be
    /// assembled.
    /// </remarks>
    public sealed class OrderPriceService
    {
        private readonly IFileService _files;
        private readonly IGroupQuoteService _quotes;

        public OrderPriceService(IFileService files, IGroupQuoteService quotes)
        {
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _quotes = quotes ?? throw new ArgumentNullException(nameof(quotes));
        }

        /// <summary>Builds the order and asks the site what it costs.</summary>
        public async Task<PricedOrder> PriceAsync(
            SiteSession session,
            CoreProject? project,
            IReadOnlyList<SiteProduct> catalog,
            CancellationToken cancellationToken = default) =>
            await PriceAsync(session, OrderDraft.From(project, catalog, _files), cancellationToken)
                .ConfigureAwait(false);

        /// <summary>
        /// Prices a set of the project's books, which is what one group of an order is.
        /// </summary>
        /// <remarks>
        /// A page that split a project into groups prices each group on its own: a group is one
        /// project on the site, and a single total over books of two formats would price a
        /// project the site is never asked to make.
        /// </remarks>
        public async Task<PricedOrder> PriceAsync(
            SiteSession session,
            IReadOnlyList<CoreBook>? books,
            IReadOnlyList<SiteProduct> catalog,
            CancellationToken cancellationToken = default) =>
            await PriceAsync(session, OrderDraft.From(books, catalog, _files), cancellationToken)
                .ConfigureAwait(false);

        private async Task<PricedOrder> PriceAsync(
            SiteSession session,
            OrderDraft draft,
            CancellationToken cancellationToken)
        {
            if (session is null)
                throw new ArgumentNullException(nameof(session));

            if (!draft.IsComplete)
                return PricedOrder.Refused(draft);

            var quote = await _quotes.QuoteAsync(session, draft.Books, cancellationToken).ConfigureAwait(false);

            return new PricedOrder(quote, quote.Problems, draft);
        }
    }
}
