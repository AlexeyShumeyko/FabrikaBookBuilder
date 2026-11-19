using System.Threading;
using System.Threading.Tasks;

namespace PhotoBook.Application.Sites.Completion
{
    /// <summary>Compares a project on the site with what the order meant to send.</summary>
    public interface IProjectCheckService
    {
        /// <summary>The comparison, or why the site could not be asked.</summary>
        /// <param name="session">The session; it fixes the site the project lives on.</param>
        /// <param name="projectId">The project to read back.</param>
        /// <param name="expected">Books and pages the order sent.</param>
        /// <param name="cancellationToken">Stops the read between requests.</param>
        Task<Result<ProjectCheck>> CheckAsync(
            SiteSession session,
            long projectId,
            ProjectExpectation expected,
            CancellationToken cancellationToken = default);
    }
}
