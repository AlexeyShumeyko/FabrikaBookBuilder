using System.Collections.Generic;

namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// One format of a product, for example 21x30, with its own options and parts.
    /// </summary>
    /// <remarks>
    /// The product page keeps the cover options at the variant and the spreads under parts;
    /// the catalog list keeps both as parts. The variant follows the product page, because
    /// that is the document that also carries the quantity settings, and keeps the cover
    /// options separately rather than inventing a part id the site never sent.
    /// </remarks>
    public sealed record ProductVariant(
        long Id,
        string Title,
        IReadOnlyList<ProductOption> CoverOptions,
        IReadOnlyList<ProductPart> Parts);
}
