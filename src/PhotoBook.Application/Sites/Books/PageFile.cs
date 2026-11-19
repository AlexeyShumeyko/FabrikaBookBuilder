namespace PhotoBook.Application.Sites.Books
{
    /// <summary>
    /// One file the customer put into a book, in the order the pages follow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The book keeps files as an ordered list and nothing else: a position is the index in
    /// that list, so moving a page is moving it in the list rather than renumbering a field.
    /// The size is here because the upload has to know what it is sending before it starts.
    /// </para>
    /// <para>
    /// The picture's width and height travel with the file because the site's own uploader
    /// sends them as <c>params[fileWidth]</c> and <c>params[fileHeight]</c>, and its project
    /// editor keeps the page's own dimensions in the page's params. Zero means the size was not
    /// known when the file was read; the upload then leaves those fields out instead of claiming
    /// a size of nothing.
    /// </para>
    /// </remarks>
    public sealed record PageFile(string Path, long LengthBytes, int Width = 0, int Height = 0);
}
