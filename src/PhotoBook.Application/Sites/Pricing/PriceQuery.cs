using System.Collections.Generic;

namespace PhotoBook.Application.Sites.Pricing
{
    /// <summary>
    /// One combination to be priced: the product, its format and what was chosen.
    /// </summary>
    /// <param name="ProductId">The product on the site.</param>
    /// <param name="ProductComponentId">The format, when the product has several.</param>
    /// <param name="Options">The chosen element of each option, by option id.</param>
    /// <param name="PartQuantities">How many of each part, by part id.</param>
    /// <remarks>
    /// Both maps are written the way the calculator page writes them, so a request from this
    /// program and one from the site are the same request - the platform prices them with the
    /// same code, and a combination that works in the browser cannot fail here for a reason
    /// invented by this model.
    /// </remarks>
    public sealed record PriceQuery(
        long ProductId,
        long? ProductComponentId,
        IReadOnlyDictionary<long, long> Options,
        IReadOnlyDictionary<long, int> PartQuantities);
}
