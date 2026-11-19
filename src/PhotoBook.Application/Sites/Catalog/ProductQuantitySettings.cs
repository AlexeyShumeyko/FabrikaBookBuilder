using System;
using System.Collections.Generic;
using System.Globalization;

namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// How one part of a product counts its quantity, in the site's own words.
    /// </summary>
    /// <param name="DisplayName">The site's label, kept as sent: "Количество разворотов".</param>
    /// <param name="Unit">What that label means; <see cref="QuantityUnit.Unknown"/> if unread.</param>
    /// <param name="Default">The range when no option-element rule applies, if the site sends one.</param>
    /// <param name="AllowsManualInput">Whether the site lets the customer type a quantity.</param>
    /// <param name="Rules">Ranges that replace the default once an element is chosen.</param>
    public sealed record ProductQuantitySettings(
        string DisplayName,
        QuantityUnit Unit,
        QuantityRange? Default,
        bool AllowsManualInput,
        IReadOnlyList<QuantityRule> Rules)
    {
        /// <summary>
        /// The quantities the chosen elements allow: the rule of a chosen element, else the
        /// part's own default.
        /// </summary>
        /// <remarks>
        /// The rule, not the default, is what the site prices: paper on a layflat starts at ten
        /// spreads while cardboard starts at two, and asking with the default alone would call a
        /// book of four spreads sellable until the site answered zero.
        /// </remarks>
        public QuantityRange? RangeFor(IReadOnlyDictionary<long, long> elements)
        {
            if (elements is null)
                throw new ArgumentNullException(nameof(elements));

            for (var index = 0; index < Rules.Count; index++)
            {
                var rule = Rules[index];

                if (!long.TryParse(rule.OptionId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var optionId))
                    continue;

                if (!elements.TryGetValue(optionId, out var elementId))
                    continue;

                if (rule.AppliesTo(rule.OptionId, elementId.ToString(CultureInfo.InvariantCulture)))
                    return rule.Range;
            }

            return Default;
        }
    }
}
