using System;
using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites.Catalog;

namespace PhotoBook.Application.Sites.Books
{
    /// <summary>
    /// A book as the customer assembled it: what product and format, what was chosen for each
    /// part, and which files go on the pages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The files are allowed to disagree with the chosen number of spreads. A book without a
    /// single photo, a book with half the spreads filled and a book with more files than spreads
    /// are all ordinary states while the customer works, and each is stored as it is instead of
    /// being padded, truncated or refused at construction. What to do about the disagreement is
    /// <see cref="BookReadiness"/>'s decision, taken once, when the book is about to be sent.
    /// </para>
    /// <para>
    /// <see cref="PagePartId"/> names the part the files fill, because "the part that holds
    /// photos" cannot be told from a title: the site labels it «Разворот» on one product and
    /// «Страница» on another.
    /// </para>
    /// </remarks>
    public sealed record Book(
        SiteProduct Product,
        ProductVariant Variant,
        long PagePartId,
        IReadOnlyList<SelectedPart> Parts,
        IReadOnlyList<PageFile> Files)
    {
        /// <summary>The choice made for the part that holds the photos, if there is one.</summary>
        public SelectedPart? PagePart =>
            Parts?.FirstOrDefault(part => part.PartId == PagePartId);

        /// <summary>How many pages the chosen quantity of the page part gives.</summary>
        public int PageSlots => PagePart?.Selection.Quantity ?? 0;

        /// <summary>Pages still without a photo; zero when the book is full or overfull.</summary>
        public int MissingPages => Math.Max(0, PageSlots - FileCount);

        /// <summary>Files beyond the last page; zero when they fit.</summary>
        public int ExtraFiles => Math.Max(0, FileCount - PageSlots);

        /// <summary>Files in the book, zero for a book the customer has not filled yet.</summary>
        public int FileCount => Files?.Count ?? 0;
    }
}
