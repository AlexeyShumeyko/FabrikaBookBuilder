using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PhotoBook.Application.Sites.Pricing;

namespace PhotoBook.Application.Sites.Books.Pricing
{
    /// <summary>What the site said about one book of an order.</summary>
    /// <param name="Number">The book's place in the order, starting at one.</param>
    /// <param name="Book">The book itself, so a message can name it.</param>
    /// <param name="Check">The price the site named, or the reason it named none.</param>
    public sealed record BookQuote(int Number, Book Book, SitePriceCheck Check)
    {
        /// <summary>Whether this book has a price the site would charge.</summary>
        public bool IsPriced => Check.IsValid;
    }

    /// <summary>The books of one order as the site priced them, with the total.</summary>
    /// <remarks>
    /// <para>
    /// The total is the sum of the books, because the price endpoint prices one combination -
    /// one book with its own spreads and options - and an order of twenty identical books is
    /// twenty of those. A book the site refused contributes nothing and is reported instead:
    /// the total of a partly priced order is still worth showing, and hiding it behind the
    /// refusal would leave the customer without the number they came for.
    /// </para>
    /// <para>
    /// The books are quoted one at a time rather than in parallel: the platform is shared, the
    /// answer is needed in order, and an order of twenty books is twenty requests the site would
    /// answer at the same speed one after another.
    /// </para>
    /// </remarks>
    public sealed record GroupQuote(IReadOnlyList<BookQuote> Books)
    {
        /// <summary>Whether every book of the order is priced; an empty order is not.</summary>
        public bool IsPriced => Books.Count > 0 && Books.All(book => book.IsPriced);

        /// <summary>The order's price, from the books the site priced.</summary>
        public decimal Total => Books.Where(book => book.IsPriced).Sum(book => book.Check.Quote!.Amount);

        /// <summary>The currency the site named, or an empty string when it named none.</summary>
        public string Currency =>
            Books.FirstOrDefault(book => book.IsPriced)?.Check.Quote!.Currency ?? string.Empty;

        /// <summary>The total as the site writes prices, for example «1 952,00 RUB».</summary>
        public string TotalText =>
            Currency.Length == 0
                ? Total.ToString("N2", Russian)
                : Total.ToString("N2", Russian) + " " + Currency;

        /// <summary>Why the order cannot go yet, book by book; empty when every book is priced.</summary>
        public IReadOnlyList<string> Problems
        {
            get
            {
                if (Books.Count == 0)
                    return new[] { "В заказе нет ни одной книги." };

                return Books
                    .Where(book => !book.IsPriced)
                    .Select(book => $"Книга {book.Number}: {book.Check.Error}")
                    .ToList();
            }
        }

        /// <summary>The site's own decimal comma, which is what the windows show.</summary>
        private static CultureInfo Russian => CultureInfo.GetCultureInfo("ru-RU");
    }
}
