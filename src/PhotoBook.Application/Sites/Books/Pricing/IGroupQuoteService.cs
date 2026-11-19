using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoBook.Application.Sites.Books.Pricing
{
    /// <summary>Asks the site what an order's books cost.</summary>
    /// <remarks>
    /// One request per book, in order, through the same endpoint the calculator page uses. The
    /// service exists so the window asks once for a whole order rather than building price
    /// requests itself and summing whatever comes back.
    /// </remarks>
    public interface IGroupQuoteService
    {
        /// <summary>The price of every book, with the order's total.</summary>
        /// <param name="session">The session; it fixes the site the price is asked on.</param>
        /// <param name="books">The order's books, in the order they are shown.</param>
        /// <param name="cancellationToken">Stops between books; nothing is kept.</param>
        Task<GroupQuote> QuoteAsync(
            SiteSession session,
            IReadOnlyList<Book> books,
            CancellationToken cancellationToken = default);
    }
}
