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

        /// <summary>
        /// The index for a quantity, counted in the part the site's own rule names.
        /// </summary>
        /// <param name="quantity">The quantity of the part being sized.</param>
        /// <param name="partQuantities">
        /// The quantity of every part of the book, when the rule counts another one - the
        /// cover grows with the spread count. Without it a rule counts the part's own quantity.
        /// </param>
        public int? IndexFor(int quantity, IReadOnlyDictionary<long, int>? partQuantities = null)
        {
            for (var index = 0; index < Rules.Count; index++)
            {
                var rule = Rules[index];
                var counted = rule.PartId > 0 && partQuantities is not null &&
                              partQuantities.TryGetValue(rule.PartId, out var other)
                    ? other
                    : quantity;

                if (rule.Covers(counted))
                    return rule.Index;
            }

            return null;
        }
    }
}
