namespace PhotoBook.Application.Sites
{
    /// <summary>Why a call to a site did not produce a document.</summary>
    public enum SiteApiErrorKind
    {
        /// <summary>The session ended, or the password was wrong.</summary>
        Unauthorized,

        /// <summary>The account may not do this.</summary>
        Forbidden,

        /// <summary>The project, product or file is not on this site.</summary>
        NotFound,

        /// <summary>The platform asked us to slow down.</summary>
        RateLimited,

        /// <summary>The file is larger than the site accepts.</summary>
        FileTooLarge,

        /// <summary>The site answered with a server error.</summary>
        ServerUnavailable,

        /// <summary>The connection dropped before the site answered.</summary>
        ConnectionLost,

        /// <summary>The site accepted the connection and never answered.</summary>
        Timeout,

        /// <summary>The site answered with nothing.</summary>
        EmptyResponse,

        /// <summary>The site answered with something that is not the expected JSON.</summary>
        MalformedResponse,

        /// <summary>Any other status the program has no better word for.</summary>
        Other
    }

    /// <summary>
    /// A failed call, in a form the caller can act on.
    /// </summary>
    /// <remarks>
    /// The program used to keep only the sentence it had composed, so a caller that needed to
    /// tell an expired session from a wrong password - or a rate limit from a broken server -
    /// had to compare Russian text. The kind and the status code are what the caller switches
    /// on; the message is what it shows.
    /// </remarks>
    public sealed record SiteApiError(SiteApiErrorKind Kind, string Message, int? StatusCode = null)
    {
        /// <summary>Whether asking again could give a different answer.</summary>
        public bool IsRetryable => Kind is SiteApiErrorKind.RateLimited
            or SiteApiErrorKind.ServerUnavailable
            or SiteApiErrorKind.ConnectionLost
            or SiteApiErrorKind.Timeout;

        /// <summary>Whether the next step is to sign in again rather than to try the call.</summary>
        public bool IsSessionExpired => Kind is SiteApiErrorKind.Unauthorized;
    }
}
