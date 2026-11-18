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
    /// <para>
    /// The cover is a part of its own with one file, and the site numbers that file first: its
    /// position is 0 and the spreads start at 1. So <see cref="Files"/> stays the spreads, which
    /// are what pricing and readiness count, and <see cref="AllFiles"/> is what the upload sends.
    /// </para>
    /// </remarks>
    public sealed record Book(
        SiteProduct Product,
        ProductVariant Variant,
        long PagePartId,
        IReadOnlyList<SelectedPart> Parts,
        IReadOnlyList<PageFile> Files,
        long CoverPartId = 0,
        PageFile? CoverFile = null)
    {
        /// <summary>The choice made for the cover part, if the format has one.</summary>
        public SelectedPart? CoverPart =>
            CoverPartId > 0 ? Parts?.FirstOrDefault(part => part.PartId == CoverPartId) : null;

        /// <summary>Whether the format asks for a cover file at all.</summary>
        public bool NeedsCover => CoverPartId > 0;

        /// <summary>
        /// The element chosen for every option of the book, whichever part asked the question.
        /// </summary>
        /// <remarks>
        /// The site reads options for the whole book, not per part: the cover's size row is
        /// chosen by the interlayer question, which belongs to the spreads part. Looking only at
        /// the cover's own answers would leave that question unanswered and the size unknown.
        /// </remarks>
        public IReadOnlyDictionary<long, long> OptionElements
        {
            get
            {
                var chosen = new Dictionary<long, long>();

                foreach (var part in Parts ?? (IReadOnlyList<SelectedPart>)Array.Empty<SelectedPart>())
                {
                    foreach (var pair in part.Selection.Elements)
                        chosen[pair.Key] = pair.Value;
                }

                return chosen;
            }
        }

        /// <summary>Whether the cover file is there to be sent.</summary>
        public bool HasCover => CoverFile is not null;

        /// <summary>The files as the site receives them: the cover first, then the spreads.</summary>
        public IReadOnlyList<PageFile> AllFiles
        {
            get
            {
                var spreads = Files ?? Array.Empty<PageFile>();
                if (CoverFile is null)
                    return spreads;

                var all = new List<PageFile>(spreads.Count + 1) { CoverFile };
                all.AddRange(spreads);
                return all;
            }
        }

        /// <summary>How many files the upload sends, the cover counted.</summary>
        public int SentFileCount => FileCount + (HasCover ? 1 : 0);

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
