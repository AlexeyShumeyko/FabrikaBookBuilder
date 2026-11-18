using System.Threading;
using System.Threading.Tasks;

namespace PhotoBook.Application.Sites.Upload
{
    /// <summary>Sends one page's file to a project on the site.</summary>
    /// <remarks>
    /// <para>
    /// One file per call, because that is what the platform accepts: the uploader sends a
    /// multipart request per page with <c>projectId</c>, <c>position</c> and <c>file</c>. The
    /// position is zero-based, as the site's own uploader sends it. It also names the picture -
    /// <c>params[fileName]</c>, <c>params[fileWidth]</c>, <c>params[fileHeight]</c> and
    /// <c>checksum</c> - and those fields are part of the request here too, because the site
    /// keeps them on the page it stores.
    /// </para>
    /// <para>
    /// The answer is read, not assumed: the endpoint replies <c>200</c> with
    /// <c>{"corrupted_file": "..."}</c> for a file it cannot use, so a success status alone
    /// means nothing here. The refusal comes back as a kind, so the screen can tell an expired
    /// session from a photograph the site could not read.
    /// </para>
    /// </remarks>
    public interface ISiteFileUploadService
    {
        /// <summary>Sends one file; the answer says whether the site could use it.</summary>
        /// <param name="filePath">The file itself.</param>
        /// <param name="width">The picture's width in pixels, or zero when it was not read.</param>
        /// <param name="height">The picture's height in pixels, or zero when it was not read.</param>
        Task<PageUpload> UploadAsync(
            SiteSession session,
            long projectId,
            long projectItemId,
            int position,
            string filePath,
            int width = 0,
            int height = 0,
            CancellationToken cancellationToken = default);
    }
}
