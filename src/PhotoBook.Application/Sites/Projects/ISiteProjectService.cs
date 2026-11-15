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
        /// Adds a book to an existing project, which is how one project holds a whole group.
        /// </summary>
        /// <param name="position">The book's place in the project, counting from one.</param>
        /// <remarks>
        /// The site creates a project with exactly one book, so a group of three needs two more.
        /// The number it answers with is what every file of that book is uploaded against.
        /// </remarks>
        Task<Result<long>> AddBookAsync(
            SiteSession session,
            long projectId,
            int position,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Names the project's parameters and its price, which the site keeps as given.
        /// </summary>
        /// <remarks>
        /// Without this call the project exists with no parameters and a price of zero, and the
        /// customer sees an order the site cannot produce. The site does not price a project by
        /// itself: the amount is the one the calculator named for its books.
        /// </remarks>
        Task<Result> SetUpAsync(
            SiteSession session,
            long projectId,
            long productId,
            ProjectParameters parameters,
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
