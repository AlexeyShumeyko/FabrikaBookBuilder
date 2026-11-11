namespace PhotoBook.Application.Sites.Upload
{
    /// <summary>Why one page did not reach the site.</summary>
    /// <remarks>
    /// The kinds are what the screen switches on: an expired session asks for the password, a
    /// broken connection or a rate limit means "send the rest again in a moment", and a file the
    /// site could not read is the customer's own photograph, which no retry will improve.
    /// </remarks>
    public enum UploadFailureKind
    {
        /// <summary>The page was accepted; there is nothing to report.</summary>
        None,

        /// <summary>The site could not use the file, or it is too large.</summary>
        File,

        /// <summary>The site refused the request's parameters or the project it named.</summary>
        Parameters,

        /// <summary>The session ended; the customer has to sign in again.</summary>
        Unauthorized,

        /// <summary>The platform asked us to slow down.</summary>
        RateLimited,

        /// <summary>The site answered with a server error.</summary>
        ServerUnavailable,

        /// <summary>The connection dropped or the site never answered.</summary>
        ConnectionLost,

        /// <summary>Any refusal the program has no better word for.</summary>
        Other
    }

    /// <summary>One page's answer: accepted, or why it was not.</summary>
    /// <param name="IsAccepted">Whether the site took the file.</param>
    /// <param name="Kind">Why not, when it did not.</param>
    /// <param name="Error">The sentence to show, or null on success.</param>
    /// <remarks>
    /// The endpoint answers <c>200</c> for a file it cannot read, so the answer is this value
    /// rather than the status code: a caller that trusted the code would count a corrupt page as
    /// uploaded and leave a hole in the book.
    /// </remarks>
    public sealed record PageUpload(bool IsAccepted, UploadFailureKind Kind, string? Error)
    {
        /// <summary>The site took the file.</summary>
        public static PageUpload Accepted { get; } = new(true, UploadFailureKind.None, null);

        /// <summary>The site did not take the file, for the stated reason.</summary>
        public static PageUpload Refused(UploadFailureKind kind, string error) =>
            new(false, kind, string.IsNullOrWhiteSpace(error) ? "Сайт не принял файл." : error);

        /// <summary>Whether signing in again is the next step rather than resending the page.</summary>
        public bool NeedsSignIn => Kind == UploadFailureKind.Unauthorized;

        /// <summary>Whether sending the same page again later could work.</summary>
        public bool CanResume => Kind is UploadFailureKind.RateLimited
            or UploadFailureKind.ServerUnavailable
            or UploadFailureKind.ConnectionLost;
    }
}
