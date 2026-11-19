namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// Reads the uploader's own document for one format into the catalog model.
    /// </summary>
    /// <remarks>
    /// The document is asked for by the two ids the customer chose, so nothing has to be
    /// recognised by name: it answers for the format that was picked and no other.
    /// </remarks>
    public interface ISiteFormatParser
    {
        Result<SiteProduct> Parse(string json, long productId, long componentId);
    }
}
