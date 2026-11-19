using System.Collections.Generic;

namespace PhotoBook.Application.Sites.Books
{
    /// <summary>
    /// The books that go to the site as one order, on one site.
    /// </summary>
    /// <remarks>
    /// The region is on the group rather than on each book because the platform prices these
    /// books on one site only: a group is what the customer confirms, and it cannot mix the two
    /// sites. Recipient and delivery details are not here - they belong to the checkout the site
    /// itself describes, and will be taken from that contract rather than guessed now.
    /// </remarks>
    public sealed record OrderGroup(SiteRegion Region, IReadOnlyList<Book> Books)
    {
        /// <summary>How many books the customer put into the order.</summary>
        public int BookCount => Books?.Count ?? 0;
    }
}
