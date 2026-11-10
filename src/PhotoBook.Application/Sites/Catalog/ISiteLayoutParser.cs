using PhotoBook.Application.Sites.Catalog.Layout;

namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// Reads the uploader's geometry document into the layout model.
    /// </summary>
    public interface ISiteLayoutParser
    {
        Result<SiteLayout> Parse(string json);
    }
}
