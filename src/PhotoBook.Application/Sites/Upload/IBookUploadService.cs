using System;
using System.Collections.Generic;
using PhotoBook.Application.Sites.Books;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoBook.Application.Sites.Upload
{
    /// <summary>Sends a book's pages to a project, one after another.</summary>
    /// <remarks>
    /// The order is the book's order and it is not parallelised: the site numbers the pages by
    /// the position in the request, and two requests in flight would decide the page order by
    /// whichever finished first.
    /// </remarks>
    public interface IBookUploadService
    {
        /// <summary>Sends the pages that are not on the site yet.</summary>
        /// <param name="session">The session; it fixes the site the project lives on.</param>
        /// <param name="projectId">The project the pages go into.</param>
        /// <param name="files">The book's pages, in page order.</param>
        /// <param name="alreadyUploaded">
        /// How many the site already has; null asks the record, so a restart continues where it
        /// stopped instead of starting the book over.
        /// </param>
        /// <param name="progress">Told after each file, for a bar that matches the site.</param>
        /// <param name="cancellationToken">Stops between files; sent files stay sent.</param>
        Task<BookUploadResult> UploadAsync(
            SiteSession session,
            long projectId,
            IReadOnlyList<PageFile> files,
            int? alreadyUploaded = null,
            IProgress<UploadProgress>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
