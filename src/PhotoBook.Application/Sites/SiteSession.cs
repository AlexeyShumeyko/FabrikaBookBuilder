using System;

namespace PhotoBook.Application.Sites
{
    /// <summary>
    /// What the program knows after a successful sign-in: which site answered, with what token,
    /// and until when.
    /// </summary>
    /// <remarks>
    /// The password is not part of this and is never stored: the platform issues a token for
    /// seven days and has no refresh endpoint, so an expired session means asking for the
    /// password again rather than keeping a secret on disk.
    /// </remarks>
    public sealed record SiteSession(SiteRegion Region, string AccessToken, DateTimeOffset ExpiresAt)
    {
        public bool IsExpiredAt(DateTimeOffset moment) => moment >= ExpiresAt;
    }
}
