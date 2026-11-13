using System;
using System.Globalization;
using PhotoBook.Application.Sites.Projects;

namespace PhotoBook.Application.Sites.Completion
{
    /// <summary>The project's page on the site of the session it was created on.</summary>
    /// <remarks>
    /// <para>
    /// The address is the product's own page plus the query parameter the site's editor reads,
    /// both taken from the site: the product page comes from the account list and the parameter
    /// is the one the editor's own code uses. Neither is written into the program, because a
    /// guessed route would open the site's front page and look like a broken link rather than a
    /// wrong one.
    /// </para>
    /// <para>
    /// The region is not a detail: the same project id means nothing on the other site, and an
    /// address built from the wrong one sends the customer to a page that cannot show it.
    /// </para>
    /// </remarks>
    public static class ProjectLink
    {
        /// <summary>The address, or an empty string when the site named no product page.</summary>
        public static string Of(SiteRegion region, SiteProjectSnapshot project)
        {
            if (region is null)
                throw new ArgumentNullException(nameof(region));
            if (project is null)
                throw new ArgumentNullException(nameof(project));

            if (project.Id <= 0 || string.IsNullOrWhiteSpace(project.ProductUrl))
                return string.Empty;

            var path = project.ProductUrl.StartsWith("/", StringComparison.Ordinal)
                ? project.ProductUrl
                : "/" + project.ProductUrl;

            return region.SiteUrl + path + "?projectId=" +
                project.Id.ToString(CultureInfo.InvariantCulture);
        }
    }
}
