using System;

namespace PhotoBook.Application.Sites.Pricing
{
    /// <summary>A price the site named, with the text it is shown as.</summary>
    /// <param name="Amount">The number the site prices the combination at.</param>
    /// <param name="Formatted">The site's own text, for example «542,00 RUB».</param>
    /// <param name="Currency">The currency the site named, or an empty string.</param>
    public sealed record SitePriceQuote(decimal Amount, string Formatted, string Currency);

    /// <summary>What the site said about a combination.</summary>
    public enum SitePriceOutcome
    {
        /// <summary>The site named a price.</summary>
        Valid,

        /// <summary>The site answered zero, which is how it says the set is not sellable.</summary>
        Invalid,

        /// <summary>The price could not be read: no connection, an error, or an answer without one.</summary>
        Error
    }

    /// <summary>
    /// The price of one combination, as one of three outcomes rather than a number.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The endpoint answers <c>200</c> with <c>price: 0</c> for a combination it will not sell,
    /// so a plain number would put «0 ₽» in front of the customer and make an unsellable set
    /// look free. Zero is therefore its own outcome, and the window can say «комбинация
    /// недоступна» instead.
    /// </para>
    /// <para>
    /// An error is not a zero either: a lost connection says nothing about the combination, and
    /// treating it as a refusal would hide a set the customer can actually order.
    /// </para>
    /// </remarks>
    public sealed record SitePriceCheck(SitePriceOutcome Outcome, SitePriceQuote? Quote, string? Error)
    {
        /// <summary>Whether the combination is priced and may be ordered.</summary>
        public bool IsValid => Outcome == SitePriceOutcome.Valid;

        public static SitePriceCheck Valid(SitePriceQuote quote)
        {
            if (quote is null)
                throw new ArgumentNullException(nameof(quote));

            return new SitePriceCheck(SitePriceOutcome.Valid, quote, null);
        }

        public static SitePriceCheck Invalid() =>
            new(SitePriceOutcome.Invalid, null, "Комбинация недоступна.");

        public static SitePriceCheck Failed(string message) =>
            new(
                SitePriceOutcome.Error,
                null,
                string.IsNullOrWhiteSpace(message) ? "Цену узнать не удалось." : message);
    }
}
