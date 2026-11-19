using System.Threading;
using System.Threading.Tasks;

namespace PhotoBook.Application.Sites.Pricing
{
    /// <summary>
    /// Asks the site what one combination costs.
    /// </summary>
    /// <remarks>
    /// The price is never computed here. The site owns its prices, discounts and rules, and a
    /// second implementation of them in this program would disagree with the invoice exactly
    /// when a customer is looking at both.
    /// </remarks>
    public interface ISitePriceService
    {
        /// <summary>The session fixes the site the price is asked on.</summary>
        Task<SitePriceCheck> CheckAsync(
            SiteSession session,
            PriceQuery query,
            CancellationToken cancellationToken = default);
    }
}
