using System.Collections.Generic;
using PhotoBook.Application.Sites.Projects;

namespace PhotoBook.Application.Sites.Completion
{
    /// <summary>What the customer's order asked the site to hold.</summary>
    /// <param name="PagesPerBook">Spreads in each book, in the order the books were sent.</param>
    /// <remarks>
    /// Page counts are what can be compared without guessing: the site's page entries carry a
    /// position and a colour space, but not the file name or its pixel size, so a check that
    /// claimed to recognise a photograph would be inventing a fact. Counting is honest.
    /// </remarks>
    public sealed record ProjectExpectation(IReadOnlyList<int> PagesPerBook)
    {
        /// <summary>Books the order puts in this project.</summary>
        public int BookCount => PagesPerBook.Count;

        /// <summary>Spreads across those books.</summary>
        public int PageCount
        {
            get
            {
                var total = 0;
                foreach (var pages in PagesPerBook)
                    total += pages;

                return total;
            }
        }
    }

    /// <summary>How a project on the site compares with what was sent.</summary>
    /// <param name="Project">The project as the site returned it.</param>
    /// <param name="Url">The project's page on the site, or an empty string when unknown.</param>
    /// <param name="Differences">What does not match, in words for the customer.</param>
    public sealed record ProjectCheck(
        SiteProjectSnapshot Project,
        string Url,
        IReadOnlyList<string> Differences)
    {
        /// <summary>Whether the site holds what the order sent.</summary>
        public bool Matches => Differences.Count == 0;

        /// <summary>Whether the customer can be sent to the project on the site.</summary>
        public bool HasUrl => Url.Length > 0;
    }
}
