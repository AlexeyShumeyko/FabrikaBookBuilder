using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PhotoBook.Application.Sites.Books.Grouping;
using PhotoBook.Application.Sites.Books.Pricing;

namespace PhotoBook.Application.Sites.Books.Submission
{
    /// <summary>What the customer reviews before the first write request of an order.</summary>
    /// <remarks>
    /// <para>
    /// The summary is built from answers already in hand - a layout and a price per group - and
    /// asks the site nothing. That is the point of it: the screen that shows this may be opened
    /// and closed as often as the customer likes, and until they press the button no request
    /// that changes anything leaves the program. The button itself belongs to the upload step
    /// (6.x); what is fixed here is the condition it will be allowed to run on.
    /// </para>
    /// <para>
    /// A group with an impossible combination or a book the site refused blocks the whole
    /// order rather than being skipped: sending the other groups would create projects the
    /// customer never sees a price for, and a half-sent order is harder to explain than a
    /// stopped one.
    /// </para>
    /// </remarks>
    public sealed record SubmissionSummary(IReadOnlyList<GroupSummary> Groups, BookGroups Layout)
    {
        /// <summary>Projects the upload would create on the site.</summary>
        public int ProjectCount => Groups.Count;

        /// <summary>Books in the order, across its projects.</summary>
        public int BookCount => Groups.Sum(group => group.BookCount);

        /// <summary>The order's price, from the groups the site priced.</summary>
        public decimal Total => Groups.Sum(group => group.Quote.Total);

        /// <summary>The total as the site writes prices, or an empty line when nothing is priced.</summary>
        public string TotalText
        {
            get
            {
                var priced = Groups.FirstOrDefault(group => group.Quote.IsPriced);
                if (priced is null)
                    return string.Empty;

                var text = Total.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"));
                var currency = priced.Quote.Currency;

                return currency.Length == 0 ? text : text + " " + currency;
            }
        }

        /// <summary>Everything standing between the order and the upload.</summary>
        public IReadOnlyList<string> Blockers =>
            Layout.Problems
                .Concat(Groups.SelectMany(group => group.Problems))
                .ToList();

        /// <summary>Whether the upload button may be pressed.</summary>
        public bool CanUpload => ProjectCount > 0 && Blockers.Count == 0;

        /// <summary>Pairs a layout with the price of each of its groups, in the same order.</summary>
        public static SubmissionSummary Build(BookGroups layout, IReadOnlyList<GroupQuote> quotes)
        {
            if (layout is null)
                throw new ArgumentNullException(nameof(layout));
            if (quotes is null)
                throw new ArgumentNullException(nameof(quotes));

            if (quotes.Count != layout.Groups.Count)
            {
                throw new ArgumentException(
                    "Every group needs exactly one quote.",
                    nameof(quotes));
            }

            var groups = new List<GroupSummary>(layout.Groups.Count);

            for (var index = 0; index < layout.Groups.Count; index++)
                groups.Add(new GroupSummary(index + 1, layout.Groups[index], quotes[index]));

            return new SubmissionSummary(groups, layout);
        }
    }
}
