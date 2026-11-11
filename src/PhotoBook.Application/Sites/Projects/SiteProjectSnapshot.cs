using System.Collections.Generic;
using System.Linq;

namespace PhotoBook.Application.Sites.Projects
{
    /// <summary>One page the site holds, as it holds it.</summary>
    /// <param name="Position">The page's place in the book, from zero.</param>
    /// <param name="Alerts">What the site complains about on this page, if anything.</param>
    public sealed record SiteProjectPage(int Position, IReadOnlyList<string> Alerts);

    /// <summary>One book of a project, with the pages the site actually has.</summary>
    /// <param name="Id">The book's id inside the project.</param>
    /// <param name="Title">Its title, which the site may leave empty.</param>
    /// <param name="Quantity">How many copies the site has.</param>
    /// <param name="Pages">The spreads the site holds, in its own order.</param>
    public sealed record SiteProjectItem(long Id, string? Title, int Quantity, IReadOnlyList<SiteProjectPage> Pages);

    /// <summary>A project read back from the site, for comparison with what was sent.</summary>
    /// <param name="Id">The project's id.</param>
    /// <param name="ProductId">The product it was created for.</param>
    /// <param name="Title">Its title as the site holds it.</param>
    /// <param name="ProductUrl">The product's page on the site, such as <c>/layflat</c>.</param>
    /// <param name="IsVisibleInAccount">Whether the account's project list shows it.</param>
    /// <param name="Items">The books it holds.</param>
    /// <remarks>
    /// This is what the site says, not what the program hoped to send. Reading it back is the
    /// only way to know that a book the uploader counted really arrived: the upload endpoint
    /// answers 200 for a file it could not read, so a count of requests is not a count of pages.
    /// </remarks>
    public sealed record SiteProjectSnapshot(
        long Id,
        long ProductId,
        string Title,
        string ProductUrl,
        bool IsVisibleInAccount,
        IReadOnlyList<SiteProjectItem> Items)
    {
        /// <summary>How many books the project holds.</summary>
        public int BookCount => Items.Count;

        /// <summary>How many spreads the project holds, across its books.</summary>
        public int PageCount => Items.Sum(item => item.Pages.Count);
    }
}
