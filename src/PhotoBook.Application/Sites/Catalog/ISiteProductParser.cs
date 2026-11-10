namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// Reads a product page into the catalog model.
    /// </summary>
    /// <remarks>
    /// A failure here is an answer, not an exception: a product page that arrives broken or in
    /// a shape the program does not know is reported to the customer as "the catalogue could
    /// not be read" rather than crashing a window.
    /// </remarks>
    public interface ISiteProductParser
    {
        Result<SiteProduct> Parse(string json, string slug);
    }
}
