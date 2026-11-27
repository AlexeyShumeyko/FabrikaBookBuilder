namespace PhotoBook.Application.Sites.Books.Files
{
    /// <summary>How much a file problem matters before a book goes to the site.</summary>
    /// <remarks>
    /// Two levels rather than three because the answers are only ever two: a file the site will
    /// refuse or print wrong stops the order, and a file that is a pixel off is something to know
    /// rather than something to fix. A file the site would trim is a refusal like any other - the
    /// customer has no third answer to give it, only to replace the file or check the format.
    /// </remarks>
    public enum FileAlertLevel
    {
        /// <summary>The site will refuse the file, or the page would be printed wrong.</summary>
        Blocking,

        /// <summary>Worth telling; nothing to decide.</summary>
        Information
    }

    /// <summary>One thing found in a book's files, in a form the window can show.</summary>
    /// <param name="Level">Whether the file stops the book or just informs.</param>
    /// <param name="PageNumber">The page the alert belongs to, starting at one; null for the book.</param>
    /// <param name="Message">What the customer reads, in the customer's words.</param>
    public sealed record FileAlert(FileAlertLevel Level, int? PageNumber, string Message);
}
