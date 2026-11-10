using System.Collections.Generic;

namespace PhotoBook.Application.Sites.Catalog.Layout
{
    /// <summary>
    /// Which page size index applies for one choice of an option, and at what quantity.
    /// </summary>
    /// <remarks>
    /// The site states the index per option element: with no interlayer the cover index grows
    /// at a different pace than with cardboard, because the spine is thicker. Matching only on
    /// the quantity would pick the wrong table for half of the books.
    /// </remarks>
    public sealed record SizeIndexCondition(
        string OptionId,
        string ElementId,
        IReadOnlyList<SizeIndexRule> Rules)
    {
        public bool Describes(string optionId, string elementId) =>
            string.Equals(OptionId, optionId, System.StringComparison.Ordinal) &&
            string.Equals(ElementId, elementId, System.StringComparison.Ordinal);

        public int? IndexFor(int spreads)
        {
            for (var index = 0; index < Rules.Count; index++)
            {
                if (Rules[index].Covers(spreads))
                    return Rules[index].Index;
            }

            return null;
        }
    }
}
