using System.Collections.Generic;

namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// One printable part of a book: the cover, the spreads, or the pages.
    /// </summary>
    /// <param name="HoldsPages">
    /// Whether the photographs go into this part. The site states it twice and differently: the
    /// product page names the unit in words («разворот», «страница»), the uploader document
    /// names the part by id. Both readings end here, so the rest of the program asks one
    /// question instead of reading labels.
    /// </param>
    public sealed record ProductPart(
        long Id,
        string Title,
        IReadOnlyList<ProductOption> Options,
        ProductQuantitySettings Quantity,
        bool HoldsPages = false);
}
