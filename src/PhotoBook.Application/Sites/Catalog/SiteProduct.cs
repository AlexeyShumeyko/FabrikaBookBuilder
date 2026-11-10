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
    public sealed record SiteProduct(
        long Id,
        string Slug,
        string Title,
        string Kind,
        IReadOnlyList<ProductVariant> Variants);
}
