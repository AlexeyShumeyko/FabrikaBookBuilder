using PhotoBook.Application.Sites.Catalog;

namespace PhotoBook.Application.Sites.Books
{
    /// <summary>
    /// What was chosen for one part of a product, tied to the part it belongs to.
    /// </summary>
    /// <remarks>
    /// <see cref="PartSelection"/> describes a choice on its own and does not know which part it
    /// is for; a book needs that link to read the chosen quantity as a number of pages.
    /// </remarks>
    public sealed record SelectedPart(long PartId, PartSelection Selection);
}
