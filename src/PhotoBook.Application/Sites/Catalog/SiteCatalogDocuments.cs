namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// The catalog documents this program reads, by the names the platform uses.
    /// </summary>
    /// <remarks>
    /// The names are here rather than typed at each call: a typo in a cache key would not fail
    /// loudly, it would quietly store one document under two names and fetch it twice.
    /// </remarks>
    public static class SiteCatalogDocuments
    {
        /// <summary>The product page: formats, options, parts, quantity rules.</summary>
        public const string Page = "page/data";

        /// <summary>The uploader's document: page geometry, margins, resolution.</summary>
        public const string Products = "products/data";
    }
}
