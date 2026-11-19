using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites.Pricing;

namespace PhotoBook.Application.Sites.Books.Pricing
{
    /// <summary>Prices an order's books one at a time and reports them in order.</summary>
    public sealed class GroupQuoteService : IGroupQuoteService
    {
        private readonly ISitePriceService _prices;

        public GroupQuoteService(ISitePriceService prices)
        {
            _prices = prices ?? throw new ArgumentNullException(nameof(prices));
        }

        public async Task<GroupQuote> QuoteAsync(
            SiteSession session,
            IReadOnlyList<Book> books,
            CancellationToken cancellationToken = default)
        {
            if (session is null)
                throw new ArgumentNullException(nameof(session));
            if (books is null)
                throw new ArgumentNullException(nameof(books));

            var quotes = new List<BookQuote>(books.Count);

            for (var index = 0; index < books.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var book = books[index] ?? throw new ArgumentException(
                    "The order holds a book that is not there.",
                    nameof(books));

                var check = await _prices
                    .CheckAsync(session, BookPriceQuery.From(book), cancellationToken)
                    .ConfigureAwait(false);

                quotes.Add(new BookQuote(index + 1, book, check));
            }

            return new GroupQuote(quotes);
        }
    }
}
