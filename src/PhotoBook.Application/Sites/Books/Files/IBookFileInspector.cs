using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoBook.Application.Sites.Books.Files
{
    /// <summary>Reads what the disk says about a book's pages, so the rules can stay free of it.</summary>
    /// <remarks>
    /// <para>
    /// The check itself takes <see cref="PageFileFacts"/> and knows nothing about files. This is
    /// the other half: it asks the file system whether a page is there and the image reader how
    /// big it is, once per page, in page order.
    /// </para>
    /// <para>
    /// It is a port rather than a static helper because the batch run of 4.3 hands it a whole
    /// order and needs to cancel halfway, and because a test that has to reach the disk to check
    /// a rule about cancellation is not a test of the rule.
    /// </para>
    /// </remarks>
    public interface IBookFileInspector
    {
        /// <summary>What the disk says about each page, in the order the pages come.</summary>
        /// <param name="files">The book's files, in page order.</param>
        /// <param name="cancellationToken">Stops between pages; the answer is thrown away.</param>
        Task<IReadOnlyList<PageFileFacts>> InspectAsync(
            IReadOnlyList<PageFile> files,
            CancellationToken cancellationToken = default);
    }
}
