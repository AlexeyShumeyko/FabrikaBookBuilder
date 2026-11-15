using System.Collections.Generic;

namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>One format a product is sold in, as the uploader names it.</summary>
    /// <param name="Id">The id the site calls a component: the book's format.</param>
    /// <param name="Name">The site's own name for it, e.g. «21x30».</param>
    public sealed record CatalogFormat(long Id, string Name);

    /// <summary>
    /// A product the uploader can order, with the formats it comes in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the list the site's own uploader works from: the products it can accept, in the
    /// order it shows them. It carries no options and no quantities - those belong to one
    /// product and one format, and are read when a format is chosen - so it stays small enough
    /// to keep whole.
    /// </para>
    /// <para>
    /// Nothing here is known to the program in advance: the products, their names, their order
    /// and their formats all arrive with the answer.
    /// </para>
    /// </remarks>
    public sealed record CatalogProduct(
        long Id,
        string Name,
        string Kind,
        int Order,
        IReadOnlyList<CatalogFormat> Formats)
    {
        /// <summary>Whether the product offers at least one format to choose.</summary>
        public bool HasFormats => Formats.Count > 0;
    }
}
