using System.Threading;
using System.Threading.Tasks;

namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// The site's catalog, read once and kept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The platform is shared by two sites and load on it is forbidden, so a product is read
    /// from the stored document when there is one and fetched only when there is not. A stored
    /// document that has aged is still used: the window has to open without a network, and the
    /// customer can decide whether yesterday's option list is good enough while the page says
    /// how old it is.
    /// </para>
    /// <para>
    /// The document is the product page the site's own calculator reads, because it is the one
    /// that carries the quantity settings and the site's words for what a quantity counts.
    /// </para>
    /// </remarks>
    public interface ISiteCatalogService
    {
        /// <summary>
        /// The product with this slug on this site, from the cache or from one request.
        /// </summary>
        /// <param name="region">The site the product is read from; it is never switched.</param>
        /// <param name="slug">The product's slug, e.g. <c>layflat</c>.</param>
        /// <param name="cancellationToken">Stops the request; nothing is stored.</param>
        Task<Result<SiteProduct>> GetProductAsync(
            SiteRegion region,
            string slug,
            CancellationToken cancellationToken = default);
    }
}
