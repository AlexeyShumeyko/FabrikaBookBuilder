using System.Collections.Generic;

namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// One printable part of a book: the cover, the spreads, or the pages.
    /// </summary>
    public sealed record ProductPart(
        long Id,
        string Title,
        IReadOnlyList<ProductOption> Options,
        ProductQuantitySettings Quantity);
}
