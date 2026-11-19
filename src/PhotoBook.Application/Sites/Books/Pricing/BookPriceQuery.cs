using System;
using System.Collections.Generic;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Application.Sites.Pricing;

namespace PhotoBook.Application.Sites.Books.Pricing
{
    /// <summary>Turns a book into the request the site's calculator sends.</summary>
    /// <remarks>
    /// <para>
    /// The calculator page sends every chosen option element and every part quantity in one
    /// query, and the platform prices exactly that shape (see <see cref="PriceQuery"/>). Writing
    /// the shape here, once, is what keeps the price this program asks for the same price the
    /// browser shows.
    /// </para>
    /// <para>
    /// A part with no quantity is left out rather than sent as zero: zero is a quantity the site
    /// never offers, and asking for one would turn an unfinished book into a refusal the
    /// customer cannot explain.
    /// </para>
    /// </remarks>
    public static class BookPriceQuery
    {
        /// <summary>The price request for one book as it stands right now.</summary>
        public static PriceQuery From(Book book)
        {
            if (book is null)
                throw new ArgumentNullException(nameof(book));

            var options = new Dictionary<long, long>();
            var quantities = new Dictionary<long, int>();

            foreach (var part in book.Parts ?? Array.Empty<SelectedPart>())
            {
                if (part is null)
                    continue;

                if (part.Selection.Quantity > 0)
                    quantities[part.PartId] = part.Selection.Quantity;

                foreach (var element in part.Selection.Elements)
                {
                    // One element per option: the first part that chose it wins, so two parts
                    // sharing an option cannot send the site two answers for one question.
                    if (!options.ContainsKey(element.Key))
                        options[element.Key] = element.Value;
                }
            }

            return new PriceQuery(book.Product.Id, book.Variant.Id, options, quantities);
        }
    }
}
