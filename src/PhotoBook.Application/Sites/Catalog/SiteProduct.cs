using System.Collections.Generic;

namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// A product as one site describes it, with every id it needs left as the site sent it.
    /// </summary>
    /// <remarks>
    /// Nothing about a product is written into the program: ids, formats, options and ranges
    /// arrive with this document. The slug is the one the caller asked for, because the product
    /// page itself does not repeat it.
    /// </remarks>
    /// <param name="IntegrationId">
    /// The uploader integration the format belongs to. The site refuses to create a project
    /// without it, and it is only in this document, so it travels with the format rather than
    /// being asked for separately at upload time.
    /// </param>
    public sealed record SiteProduct(
        long Id,
        string Slug,
        string Title,
        string Kind,
        IReadOnlyList<ProductVariant> Variants,
        long IntegrationId = 0);
}
