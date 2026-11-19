using System.Threading;
using System.Threading.Tasks;

namespace PhotoBook.Application.Sites
{
    /// <summary>
    /// Reads the account's name and address from the site the session was made on.
    /// </summary>
    /// <remarks>
    /// A profile is a convenience, not a credential: failing to read it must not sign anybody
    /// out or block a window. The caller falls back to the address the session carries, which
    /// is always known.
    /// </remarks>
    public interface ISiteProfileService
    {
        /// <summary>The account's profile, or why it could not be read.</summary>
        Task<Result<SiteProfile>> GetAsync(
            SiteSession session,
            CancellationToken cancellationToken = default);
    }
}
