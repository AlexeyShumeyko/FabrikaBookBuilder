using PhotoBook.Application.Sites.Catalog.Layout;

namespace PhotoBook.Application.Sites.Books.Files
{
    /// <summary>What the disk said about one page's file, before any rule looks at it.</summary>
    /// <param name="Path">Where the file was taken from.</param>
    /// <param name="Exists">Whether the file is there right now.</param>
    /// <param name="Size">The picture's pixel size, or null when it could not be read.</param>
    /// <remarks>
    /// The facts are gathered outside and arrive here as values, so the rules stay free of the
    /// disk and are tested without one. A file that is missing and a file that is there but
    /// unreadable are kept apart on purpose: the first is a moved or deleted photograph, the
    /// second is a damaged one, and the customer is told which.
    /// </remarks>
    public sealed record PageFileFacts(string Path, bool Exists, PixelSize? Size);
}
