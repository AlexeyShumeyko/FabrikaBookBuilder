namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// Reads the uploader's product list into the catalog model.
    /// </summary>
    /// <remarks>
    /// A list that cannot be read is an answer, not an exception: the order page says the
    /// catalog did not open and keeps everything the customer has already chosen.
    /// </remarks>
    public interface ISiteProductsParser
    {
        Result<System.Collections.Generic.IReadOnlyList<CatalogProduct>> Parse(string json);
    }
}
