using System.Threading;
using System.Threading.Tasks;

namespace PhotoBook.Application.Sites.Upload
{
    /// <summary>Sends one page's file to a project on the site.</summary>
    /// <remarks>
    /// <para>
    /// One file per call, because that is what the platform accepts: the uploader sends a
    /// multipart request per page with <c>projectId</c>, <c>position</c> and <c>file</c>. The
    /// position is zero-based, as the site's own uploader sends it.
    /// </para>
    /// <para>
    /// The answer is read, not assumed: the endpoint replies <c>200</c> with
    /// <c>{"corrupted_file": "..."}</c> for a file it cannot use, so a success status alone
    /// means nothing here.
    /// </para>
    /// </remarks>
    public interface ISiteFileUploadService
    {
        /// <summary>Sends one file; the answer says whether the site could use it.</summary>
        Task<Result> UploadAsync(
            SiteSession session,
            long projectId,
            int position,
            string filePath,
            CancellationToken cancellationToken = default);
    }
}
