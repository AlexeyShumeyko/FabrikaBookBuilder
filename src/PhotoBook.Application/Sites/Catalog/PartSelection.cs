using System.Collections.Generic;

namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// What the customer picked for one part of a book, and how many.
    /// </summary>
    /// <remarks>
    /// An option offers one element at a time: there is no "two kinds of lamination" in the
    /// product page, and the site does not send a flag saying otherwise. The selection is
    /// therefore a map from option to the one element chosen, plus the quantity of the part.
    /// </remarks>
    public sealed record PartSelection(IReadOnlyDictionary<long, long> Elements, int Quantity)
    {
        public long? ElementOf(long optionId) =>
            Elements.TryGetValue(optionId, out var elementId) ? elementId : null;
    }
}
