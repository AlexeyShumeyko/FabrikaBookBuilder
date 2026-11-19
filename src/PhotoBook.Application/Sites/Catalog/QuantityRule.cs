using System;
using System.Collections.Generic;

namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// The range that applies once a particular element of a particular option is chosen.
    /// </summary>
    /// <remarks>
    /// The site sends these as a flat list keyed by option and element ids, not attached to the
    /// option itself. On a layflat the same option allows 2-20 spreads with no interlayer and
    /// 2-12 with one-millimetre cardboard, so a single range per product would be wrong for
    /// both and a range per option would be wrong for one.
    /// </remarks>
    public sealed record QuantityRule(
        string OptionId,
        IReadOnlyList<string> ElementIds,
        QuantityRange Range)
    {
        /// <summary>Whether this rule is the one that governs the given choice.</summary>
        public bool AppliesTo(string optionId, string elementId) =>
            string.Equals(OptionId, optionId, StringComparison.Ordinal) &&
            Contains(ElementIds, elementId);

        private static bool Contains(IReadOnlyList<string> ids, string id)
        {
            for (var index = 0; index < ids.Count; index++)
            {
                if (string.Equals(ids[index], id, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
