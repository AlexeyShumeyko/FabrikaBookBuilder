using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites.Books;

namespace PhotoBook.Application.Sites.Projects
{
    /// <summary>Creates the site's projects for an order, never twice for one group.</summary>
    public interface IProjectCreationService
    {
        /// <summary>One project per group, skipping the ones an earlier run already made.</summary>
        /// <param name="session">The session; it fixes the site the projects are made on.</param>
        /// <param name="groups">The groups to create, with what the site needs for each.</param>
        /// <param name="cancellationToken">Stops between groups; finished projects stay.</param>
        Task<ProjectCreationPlan> CreateAsync(
            SiteSession session,
            IReadOnlyList<GroupRequest> groups,
            CancellationToken cancellationToken = default);
    }

    /// <summary>A group plus the site's identifiers for the project it becomes.</summary>
    /// <param name="Group">The books that go into one project.</param>
    /// <param name="Request">The product, format and integration the site is asked for.</param>
    public sealed record GroupRequest(OrderGroup Group, CreateProjectRequest Request);
}
