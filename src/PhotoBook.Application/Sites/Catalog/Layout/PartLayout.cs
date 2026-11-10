using System.Collections.Generic;

namespace PhotoBook.Application.Sites.Catalog.Layout
{
    /// <summary>
    /// How one part of a book is sized: its page, the margins around it and its error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A cover is sized from a table of indexes and also reports a safety area, where a file
    /// must not put type; an inner page is sized once for the whole run and its margins are
    /// called cutting lines. Both are kept as the site sent them rather than normalised into
    /// one shape, because the two are not the same thing and the difference is what the
    /// interface has to explain when a file is refused.
    /// </para>
    /// <para>
    /// The index table belongs to the part, not to the product: it is named by the part's own
    /// attribute id, so a book with two sized parts keeps two tables instead of one that
    /// would have to be told apart by guessing.
    /// </para>
    /// </remarks>
    public sealed record PartLayout(
        long PartId,
        string Title,
        IReadOnlyList<PageSizeRange> Sizes,
        PageSize? FixedSize,
        PrintMargins FixedBleeds,
        IReadOnlyList<SizeIndexCondition> SizeIndexes,
        PrintMargins Safety,
        double ErrorMm)
    {
        /// <summary>The entry for an index, or the single fixed one when the part is not indexed.</summary>
        public PageSizeRange? EntryFor(int? index)
        {
            if (FixedSize is not null)
                return new PageSizeRange(0, FixedSize, FixedBleeds);

            if (index is not { } wanted)
                return null;

            for (var position = 0; position < Sizes.Count; position++)
            {
                if (Sizes[position].Index == wanted)
                    return Sizes[position];
            }

            return null;
        }

        /// <summary>
        /// The index the cover size is looked up by, for a quantity and one choice of option.
        /// </summary>
        public int? IndexFor(int spreads, string? optionId, string? elementId)
        {
            if (optionId is null || elementId is null)
                return null;

            for (var index = 0; index < SizeIndexes.Count; index++)
            {
                var condition = SizeIndexes[index];
                if (condition.Describes(optionId, elementId))
                    return condition.IndexFor(spreads);
            }

            return null;
        }
    }
}
