namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// Checks a selection against the product's own rules before anything is sent to the site.
    /// </summary>
    /// <remarks>
    /// The site refuses an impossible combination with a price of zero or a bare 422, and it
    /// refuses it after a round trip. The same rules are already in the catalog, so the answer
    /// is known here and can be shown without asking.
    /// </remarks>
    public interface IPartSelectionValidator
    {
        PartCheck Check(ProductPart part, PartSelection selection);
    }
}
