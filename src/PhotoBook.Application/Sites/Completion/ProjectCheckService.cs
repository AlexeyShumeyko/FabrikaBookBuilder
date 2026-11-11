using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites.Projects;

namespace PhotoBook.Application.Sites.Completion
{
    /// <summary>Compares a project read back from the site with what the order sent.</summary>
    /// <remarks>
    /// <para>
    /// A difference is reported, never repaired: deleting pages to make the numbers agree would
    /// remove what the site really has, and the customer is the one who decides whether a book
    /// with a missing spread is worth keeping.
    /// </para>
    /// <para>
    /// Everything checked here is something the site actually states - book count, page count,
    /// page positions and the alerts it puts on a page. Anything stronger would be a guess about
    /// photographs the read model does not name.
    /// </para>
    /// </remarks>
    public sealed class ProjectCheckService : IProjectCheckService
    {
        private readonly ISiteProjectService _projects;

        public ProjectCheckService(ISiteProjectService projects)
        {
            _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        }

        public async Task<Result<ProjectCheck>> CheckAsync(
            SiteSession session,
            long projectId,
            ProjectExpectation expected,
            CancellationToken cancellationToken = default)
        {
            if (session is null)
                throw new ArgumentNullException(nameof(session));
            if (expected is null)
                throw new ArgumentNullException(nameof(expected));
            if (projectId <= 0)
                throw new ArgumentOutOfRangeException(nameof(projectId), "У проекта нет номера.");

            var read = await _projects
                .ReadAsync(session, projectId, cancellationToken)
                .ConfigureAwait(false);

            if (!read.IsSuccess || read.Value is null)
                return Result<ProjectCheck>.Fail(read.Error ?? "Сайт не отдал проект для сверки.");

            var project = read.Value;
            var differences = new List<string>();

            if (!project.IsVisibleInAccount)
                differences.Add("Проекта нет в списке проектов аккаунта — сохраните его на сайте.");

            if (project.BookCount != expected.BookCount)
            {
                differences.Add(
                    "В проекте " + project.BookCount + " книг, отправляли " + expected.BookCount + ".");
            }

            var books = Math.Min(project.BookCount, expected.BookCount);

            for (var index = 0; index < books; index++)
                differences.AddRange(CompareBook(index + 1, project.Items[index], expected.PagesPerBook[index]));

            return Result<ProjectCheck>.Ok(new ProjectCheck(project, Link(session.Region, project), differences));
        }

        /// <summary>What one book on the site has to say about itself.</summary>
        private static IEnumerable<string> CompareBook(int number, SiteProjectItem book, int expectedPages)
        {
            if (book.Pages.Count != expectedPages)
            {
                yield return "Книга " + number + ": на сайте " + book.Pages.Count +
                    " страниц, отправляли " + expectedPages + ".";
            }

            // The site orders spreads by the position it holds, so the list itself has to read
            // 0, 1, 2 and so on; sorting it first would hide exactly the fault being looked for.
            if (book.Pages.Where((page, index) => page.Position != index).Any())
            {
                yield return "Книга " + number + ": страницы пронумерованы не подряд (" +
                    string.Join(", ", book.Pages.Select(page => (page.Position + 1).ToString())) +
                    ") — порядок разворотов на сайте другой.";
            }

            foreach (var page in book.Pages.Where(page => page.Alerts.Count > 0))
            {
                yield return "Книга " + number + ", страница " + (page.Position + 1) + ": " +
                    string.Join("; ", page.Alerts);
            }
        }

        /// <summary>
        /// The project's page on the site, built from the product page the site names and the
        /// query parameter its own editor reads.
        /// </summary>
        private static string Link(SiteRegion region, SiteProjectSnapshot project)
        {
            if (project.Id <= 0 || string.IsNullOrWhiteSpace(project.ProductUrl))
                return string.Empty;

            var path = project.ProductUrl.StartsWith("/", StringComparison.Ordinal)
                ? project.ProductUrl
                : "/" + project.ProductUrl;

            return region.SiteUrl + path + "?projectId=" + project.Id;
        }
    }
}
