using System.Collections.Generic;

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
        IReadOnlyList<QuantityRule> Rules);
}
