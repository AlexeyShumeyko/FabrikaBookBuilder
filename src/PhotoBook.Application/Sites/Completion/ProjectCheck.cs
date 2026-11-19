using System.Collections.Generic;
using PhotoBook.Application.Sites.Projects;

namespace PhotoBook.Application.Sites.Completion
{
    /// <summary>What one book of the order should look like on the site.</summary>
    /// <param name="HasCover">
    /// Whether a cover was sent for this book. The site keeps it at position zero, ahead of the
    /// spreads, so it is counted on its own rather than as one more spread.
    /// </param>
    /// <param name="Spreads">Spreads sent, the cover not counted; they sit at positions one and up.</param>
    public sealed record ExpectedBook(bool HasCover, int Spreads);

    /// <summary>What the customer's order asked the site to hold.</summary>
    /// <param name="Books">Each book's cover and spreads, in the order the books were sent.</param>
    /// <remarks>
    /// Counts are what can be compared without guessing: the site's page entries carry a
    /// position and a colour space, but not the file name or its pixel size, so a check that
    /// claimed to recognise a photograph would be inventing a fact. Counting is honest, and the
    /// cover and the spreads are counted apart because the site stores them apart.
    /// </remarks>
    public sealed record ProjectExpectation(IReadOnlyList<ExpectedBook> Books)
    {
        /// <summary>Books the order puts in this project.</summary>
        public int BookCount => Books.Count;

        /// <summary>Spreads across those books, the covers not counted.</summary>
        public int SpreadCount
        {
            get
            {
                var total = 0;
                foreach (var book in Books)
                    total += book.Spreads;

                return total;
            }
        }

        /// <summary>Covers sent, which is at most one per book.</summary>
        public int CoverCount
        {
            get
            {
                var total = 0;
                foreach (var book in Books)
                {
                    if (book.HasCover)
                        total++;
                }

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
