namespace PhotoBook.Application.Sites.Books.Files
{
    /// <summary>How much a file problem matters before a book goes to the site.</summary>
    /// <remarks>
    /// Three levels rather than one because the three answers are different: a missing file
    /// cannot be sent at all, an over-wide one is the customer's decision, and a file that is a
    /// pixel off is something to know rather than something to fix.
    /// </remarks>
    public enum FileAlertLevel
    {
        /// <summary>The site will refuse the file, or the page would be printed wrong.</summary>
        Blocking,

        /// <summary>The book can go, but only after the customer has said yes to this.</summary>
        NeedsConfirmation,

        /// <summary>Worth telling; nothing to decide.</summary>
        Information
    }

    /// <summary>One thing found in a book's files, in a form the window can show.</summary>
    /// <param name="Level">Whether the file stops the book, asks a question or just informs.</param>
    /// <param name="PageNumber">The page the alert belongs to, starting at one; null for the book.</param>
    /// <param name="Message">What the customer reads, in the customer's words.</param>
    public sealed record FileAlert(FileAlertLevel Level, int? PageNumber, string Message);
}
