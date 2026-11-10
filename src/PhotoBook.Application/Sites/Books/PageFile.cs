namespace PhotoBook.Application.Sites.Books
{
    /// <summary>
    /// One file the customer put into a book, in the order the pages follow.
    /// </summary>
    /// <remarks>
    /// The book keeps files as an ordered list and nothing else: a position is the index in
    /// that list, so moving a page is moving it in the list rather than renumbering a field.
    /// The size is here because the upload has to know what it is sending before it starts.
    /// </remarks>
    public sealed record PageFile(string Path, long LengthBytes);
}
