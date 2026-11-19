using System;
using System.Collections.Generic;
using PhotoBook.Application.Sites.Books;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoBook.Application.Sites.Upload
{
    /// <summary>Sends a book's pages to a project, a couple of them at a time.</summary>
    /// <remarks>
    /// The order is the book's order because every request names its own position: the site
    /// places the page where it is told, so two files in flight cannot decide the order by
    /// whichever finished first. The window is small on purpose - the platform is shared, and
    /// two files in flight already stop the run from paying the site's own work on one page
    /// before it starts the next.
    /// </remarks>
    public interface IBookUploadService
    {
        /// <summary>Sends the pages that are not on the site yet.</summary>
        /// <param name="session">The session; it fixes the site the project lives on.</param>
        /// <param name="projectId">The project the pages go into.</param>
        /// <param name="projectItemId">
        /// The book inside that project, or zero when the project holds one. Files of another
        /// book uploaded without it would replace this book's pages position by position.
        /// </param>
        /// <param name="files">The book's pages, in page order.</param>
        /// <param name="alreadyUploaded">
        /// How many the site already has; null asks the record, so a restart continues where it
        /// stopped instead of starting the book over.
        /// </param>
        /// <param name="progress">Told after each file, for a bar that matches the site.</param>
        /// <param name="projectItemId">The book inside the project; zero for the first one.</param>
        /// <param name="cancellationToken">Stops between files; sent files stay sent.</param>
        Task<BookUploadResult> UploadAsync(
            SiteSession session,
            long projectId,
            IReadOnlyList<PageFile> files,
            int? alreadyUploaded = null,
            IProgress<UploadProgress>? progress = null,
            long projectItemId = 0,
            CancellationToken cancellationToken = default);
    }
}
