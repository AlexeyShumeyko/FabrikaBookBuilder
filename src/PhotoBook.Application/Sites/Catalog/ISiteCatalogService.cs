using System.Collections.Generic;
using PhotoBook.Application.Sites.Catalog.Layout;
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
        /// The products this site's uploader accepts, with the formats each comes in.
        /// </summary>
        /// <remarks>
        /// The list is the uploader's own, so it says what can actually be ordered rather than
        /// what the marketing pages show. It is read once and kept: the assortment changes a few
        /// times a year, not between two visits of the order page.
        /// </remarks>
        Task<Result<IReadOnlyList<CatalogProduct>>> GetProductsAsync(
            SiteRegion region,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// The product with this slug on this site, from the cache or from one request.
        /// </summary>
        /// <param name="region">The site the product is read from; it is never switched.</param>
        /// <param name="slug">The product's slug, e.g. <c>layflat</c>.</param>
        /// <param name="cancellationToken">Stops the request; nothing is stored.</param>
        /// <summary>
        /// The uploader's own document for one format, read by the two ids the customer chose.
        /// </summary>
        /// <remarks>
        /// This is the document the uploader itself loads: it names the part that holds pages
        /// and lists the options that have to be answered before the site will price the book.
        /// It is public, so choosing a format costs no sign-in.
        /// </remarks>
        Task<Result<SiteProduct>> GetFormatAsync(
            SiteRegion region,
            long productId,
            long componentId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// The geometry of one format: the page size, its bleed and the pixel size a file must
        /// be, as the uploader's own document states them.
        /// </summary>
        /// <remarks>
        /// It comes from the same document <see cref="GetFormatAsync"/> reads, so asking for
        /// both costs one request: the file check needs the geometry, and a second shape of the
        /// same answer would be a second place for the site's numbers to drift apart.
        /// </remarks>
        Task<Result<SiteLayout>> GetLayoutAsync(
            SiteRegion region,
            long productId,
            long componentId,
            CancellationToken cancellationToken = default);

        Task<Result<SiteProduct>> GetProductAsync(
            SiteRegion region,
            string slug,
            CancellationToken cancellationToken = default);
    }
}
