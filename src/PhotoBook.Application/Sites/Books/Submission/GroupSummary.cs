using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites.Books.Pricing;

namespace PhotoBook.Application.Sites.Books.Submission
{
    /// <summary>One group of an order as the customer reviews it before anything is sent.</summary>
    /// <param name="Number">The group's place in the order, starting at one.</param>
    /// <param name="Group">The books that will become one project on the site.</param>
    /// <param name="Quote">What the site said that group costs.</param>
    public sealed record GroupSummary(int Number, OrderGroup Group, GroupQuote Quote)
    {
        /// <summary>Books that go into this project.</summary>
        public int BookCount => Group.BookCount;

        /// <summary>The product and format as the site names them.</summary>
        public string FormatName
        {
            get
            {
                var book = Group.Books.FirstOrDefault();
                return book is null
                    ? "—"
                    : book.Product.Title + " " + book.Variant.Title;
            }
        }

        /// <summary>How many spreads the group's books have, as one number or a span.</summary>
        /// <remarks>
        /// A single span rather than a list: a group is one project with books of one format,
        /// so the customer is checking "ten to eighteen spreads each", not a page of numbers.
        /// A difference inside one group is itself worth seeing, which is why a span is shown
        /// instead of the average or the first book's count.
        /// </remarks>
        public string PagesText
        {
            get
            {
                var pages = Group.Books
                    .Select(book => book.PageSlots)
                    .Distinct()
                    .OrderBy(slots => slots)
                    .ToList();

                if (pages.Count == 0)
                    return "Разворотов: нет данных";

                return pages.Count == 1
                    ? "Разворотов: " + pages[0]
                    : "Разворотов: " + pages[0] + "–" + pages[pages.Count - 1];
            }
        }

        /// <summary>The group's price as the site writes it, or a dash when it named none.</summary>
        public string PriceText => Quote.IsPriced ? Quote.TotalText : "Цена не получена";

        /// <summary>Why this group cannot go, in the customer's words.</summary>
        public IReadOnlyList<string> Problems =>
            Quote.Problems.Select(problem => "Группа " + Number + ": " + problem).ToList();
    }
}
