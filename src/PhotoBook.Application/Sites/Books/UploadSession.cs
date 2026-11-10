using System;

namespace PhotoBook.Application.Sites.Books
{
    /// <summary>
    /// The state of one book's upload: which site and project it goes to, and how far it got.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The region and the project are fixed when the session starts and travel with it. An
    /// upload that continued on the other site would put the files into a project the order
    /// never mentions, so the session is where the region is remembered rather than re-derived
    /// at each file.
    /// </para>
    /// <para>
    /// Progress counts files, not bytes: the platform accepts one file per request and a
    /// resumed session continues at the next file, so a byte counter would have nothing to
    /// resume from.
    /// </para>
    /// </remarks>
    public sealed record UploadSession(
        SiteRegion Region,
        long SiteProjectId,
        int FilesTotal,
        int FilesUploaded)
    {
        /// <summary>Files still to send.</summary>
        public int Remaining => Math.Max(0, FilesTotal - FilesUploaded);

        /// <summary>Whether every file of the book reached the project.</summary>
        public bool IsComplete => FilesUploaded >= FilesTotal;

        /// <summary>The same session after one more file was accepted.</summary>
        public UploadSession WithUploadedFile() =>
            this with { FilesUploaded = Math.Min(FilesTotal, FilesUploaded + 1) };
    }
}
