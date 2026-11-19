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
    /// <param name="AccountEmail">
    /// The address the customer signed in with, kept so the program can say whose account it
    /// is without another request. It is not a secret, and it is not the password.
    /// </param>
    public sealed record SiteSession(
        SiteRegion Region,
        string AccessToken,
        DateTimeOffset ExpiresAt,
        string? AccountEmail = null)
    {
        public bool IsExpiredAt(DateTimeOffset moment) => moment >= ExpiresAt;
    }
}
