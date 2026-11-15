using PhotoBook.Application.Sites.Books;

namespace PhotoBook.Application.Sites.Upload
{
    /// <summary>Remembers how many pages of a project the site already accepted.</summary>
    /// <remarks>
    /// A file on the customer's profile rather than memory: a dropped connection, a closed
    /// program or a restart must not turn "send the rest" into sending ten spreads again on top
    /// of the ones the site already holds.
    /// </remarks>
    public interface IAcceptedFilesStore
    {
        /// <summary>How many pages the site took for this book, or zero when it is unknown.</summary>
        /// <param name="region">The site the project lives on.</param>
        /// <param name="projectId">The project the pages go into.</param>
        /// <param name="itemId">The book inside that project; zero when the project holds one.</param>
        /// <param name="filesTotal">How many pages the book has now, to tell it from another book.</param>
        int Accepted(SiteRegion region, long projectId, long itemId, int filesTotal);

        /// <summary>Writes the count down; fails when it could not be written.</summary>
        Result Remember(UploadSession session);

        /// <summary>Drops the record of a book that is fully uploaded.</summary>
        Result Forget(SiteRegion region, long projectId, long itemId);
    }
}
