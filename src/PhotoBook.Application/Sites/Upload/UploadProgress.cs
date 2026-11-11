using System;
using PhotoBook.Application.Sites.Books;

namespace PhotoBook.Application.Sites.Upload
{
    /// <summary>How far a book's upload got, in files rather than bytes.</summary>
    /// <param name="FilesTotal">Files the book has.</param>
    /// <param name="FilesUploaded">Files the site accepted.</param>
    /// <param name="CurrentFile">The file being sent now, or an empty string between files.</param>
    /// <remarks>
    /// Files, not bytes: the platform accepts one file per request and resumes at the next one,
    /// so a byte counter would leave nothing to resume from.
    /// </remarks>
    public sealed record UploadProgress(int FilesTotal, int FilesUploaded, string CurrentFile);

    /// <summary>How a book's upload ended.</summary>
    public enum UploadState
    {
        /// <summary>Every file reached the project.</summary>
        Completed,

        /// <summary>The site refused a file, or the connection broke; the rest were not sent.</summary>
        Failed,

        /// <summary>The customer stopped it; files already sent stay on the site.</summary>
        Cancelled
    }

    /// <summary>The end of a book's upload, with the place a retry starts from.</summary>
    /// <param name="State">Completed, failed or cancelled.</param>
    /// <param name="Session">The site, the project and how many files the site accepted.</param>
    /// <param name="Error">Why it stopped, in words for the customer; null on success.</param>
    /// <param name="Kind">Which kind of refusal stopped it, for the screen to act on.</param>
    /// <param name="Notice">What went wrong without stopping the upload, such as a lost record.</param>
    public sealed record BookUploadResult(
        UploadState State,
        UploadSession Session,
        string? Error,
        UploadFailureKind Kind = UploadFailureKind.None,
        string? Notice = null)
    {
        /// <summary>Whether every file of the book is on the site.</summary>
        public bool IsComplete => State == UploadState.Completed;

        /// <summary>Whether the customer stopped it themselves.</summary>
        public bool WasCancelled => State == UploadState.Cancelled;

        /// <summary>Whether the next step is signing in again rather than resending.</summary>
        public bool NeedsSignIn => Kind == UploadFailureKind.Unauthorized;

        /// <summary>Whether sending the rest later could work without anything being fixed.</summary>
        public bool CanResume => Kind is UploadFailureKind.RateLimited
            or UploadFailureKind.ServerUnavailable
            or UploadFailureKind.ConnectionLost;
    }
}
