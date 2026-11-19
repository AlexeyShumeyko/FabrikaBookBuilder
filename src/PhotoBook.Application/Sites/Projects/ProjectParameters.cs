using System;
using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites.Books;

namespace PhotoBook.Application.Sites.Projects
{
    /// <summary>The parameters and the price the site keeps against a project.</summary>
    /// <remarks>
    /// <para>
    /// The site does not work a project's price out on its own: the client sends the chosen
    /// elements, the number of books and the amount the calculator named, and the site stores
    /// them as they are. A project without them is listed with no parameters and a price of
    /// zero, which the customer reads as an order that did not go through.
    /// </para>
    /// <para>
    /// The quantity the site wants is the number of books in the project, not the number of
    /// spreads: spreads are a part quantity, and they travel in the same body. The price is for
    /// all those books together; the site keeps it as sent and never multiplies it by the count.
    /// </para>
    /// </remarks>
    public sealed record ProjectParameters(
        long VariantId,
        int Books,
        IReadOnlyDictionary<long, long> Options,
        IReadOnlyDictionary<long, int> PartQuantities,
        decimal Price)
    {
        /// <summary>
        /// The parameters for a group of books, or null when there is nothing the site can keep:
        /// no books, no parts or no price.
        /// </summary>
        public static ProjectParameters? For(Book book, int books, decimal price)
        {
            if (book is null)
                throw new ArgumentNullException(nameof(book));

            if (books <= 0 || price <= 0)
                return null;

            var parts = book.Parts ?? Array.Empty<SelectedPart>();
            if (parts.Count == 0)
                return null;

            var options = new Dictionary<long, long>();
            var quantities = new Dictionary<long, int>(parts.Count);

            foreach (var part in parts)
            {
                quantities[part.PartId] = part.Selection.Quantity;

                foreach (var chosen in part.Selection.Elements ?? new Dictionary<long, long>())
                    options[chosen.Key] = chosen.Value;
            }

            return new ProjectParameters(book.Variant.Id, books, options, quantities, price);
        }
    }
}
