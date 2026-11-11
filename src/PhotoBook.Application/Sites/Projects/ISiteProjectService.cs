using System.Threading;
using System.Threading.Tasks;

namespace PhotoBook.Application.Sites.Projects
{
    /// <summary>What the site needs to create one project: the product, its format, its integration.</summary>
    /// <param name="ProductId">The product on the site.</param>
    /// <param name="ProductComponentId">The format of that product.</param>
    /// <param name="IntegrationId">The uploader integration the project belongs to.</param>
    /// <remarks>
    /// The integration is the site's own id for "the photo book uploader on this product"; the
    /// endpoint answers 404 without it. It comes from the uploader's document rather than from
    /// a constant here, because it differs between products and formats.
    /// </remarks>
    public sealed record CreateProjectRequest(long ProductId, long ProductComponentId, long IntegrationId);

    /// <summary>Creates one project on the site, reads it back, and reports what came back.</summary>
    /// <remarks>
    /// A project is a write: the platform creates a new one for every accepted call and has no
    /// way to recognise a repeat. The caller is therefore the one that must not ask twice, which
    /// is what <see cref="ProjectCreationService"/> is for.
    /// </remarks>
    public interface ISiteProjectService
    {
        /// <summary>The created project, or why the site refused to create one.</summary>
        Task<Result<SiteProject>> CreateAsync(
            SiteSession session,
            CreateProjectRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// The project as the site holds it now: its books, its pages and whether the account's
        /// list shows it. Used to compare an upload with what arrived.
        /// </summary>
        Task<Result<SiteProjectSnapshot>> ReadAsync(
            SiteSession session,
            long projectId,
            CancellationToken cancellationToken = default);
    }
}
