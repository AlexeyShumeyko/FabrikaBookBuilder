using System.Threading;
using System.Threading.Tasks;

namespace PhotoBook.Application.Sites
{
    /// <summary>
    /// Signing in to the platform, without the caller having to know which site the account
    /// belongs to.
    /// </summary>
    /// <remarks>
    /// The customer types an email and a password; the program asks both sites at the same time
    /// and keeps the answer. It never asks the customer to pick a site, and it never guesses
    /// one from an IP address: a person in Belarus can hold an account on the .com site, and the
    /// guess would be wrong exactly when it matters.
    /// </remarks>
    public interface ISiteAuthService
    {
        /// <summary>
        /// Signs in on whichever site accepts the credentials.
        /// </summary>
        /// <remarks>
        /// A wrong password is an ordinary answer, not an exception: the caller shows the reason.
        /// </remarks>
        Task<Result<SiteSession>> SignInAsync(
            string email,
            string password,
            CancellationToken cancellationToken = default);
    }
}
