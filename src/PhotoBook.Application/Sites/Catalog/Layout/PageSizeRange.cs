namespace PhotoBook.Application.Sites.Catalog.Layout
{
    /// <summary>
    /// A page size the site asks for, keyed by the index the product attribute computes.
    /// </summary>
    /// <remarks>
    /// The cover of a layflat widens with the spine: 428 mm at eight spreads, 438 mm at
    /// eighteen. The site does not key that by the number of spreads directly - an attribute
    /// maps the quantity to an index first - so the index is what a size is looked up by. The
    /// margins belong to the range rather than to the part, because a thicker spine is bled
    /// differently on some products.
    /// </remarks>
    public sealed record PageSizeRange(int Index, PageSize Size, PrintMargins Bleeds);
}
