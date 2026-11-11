using System;
using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Grouping;
using PhotoBook.Application.Sites.Books.Pricing;
using PhotoBook.Application.Sites.Books.Submission;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Application.Sites.Pricing;

namespace PhotoBook.Application.Tests.Sites.Books.Submission
{
    /// <summary>
    /// The roadmap's criterion lives here: nothing may be written before the customer has seen
    /// what is about to be written, and an order with a refused book is not something to send.
    /// </summary>
    public class SubmissionSummaryTests
    {
        private static Book Book(long variantId, string title, int spreads) =>
            new(
                new SiteProduct(4494, "layflat", "Лайфлат", "photo-book", new List<ProductVariant>()),
                new ProductVariant(variantId, title, new List<ProductOption>(), new List<ProductPart>()),
                PagePartId: 94,
                new List<SelectedPart>
                {
                    new(94, new PartSelection(new Dictionary<long, long>(), spreads))
                },
                new List<PageFile>());

        private static GroupQuote Quote(params decimal[] amounts) =>
            new(amounts
                .Select((amount, index) => new BookQuote(
                    index + 1,
                    Book(9255, "21x30", 10),
                    SitePriceCheck.Valid(new SitePriceQuote(amount, "x", "RUB"))))
                .ToList());

        private static GroupQuote Refused() =>
            new(new List<BookQuote>
            {
                new(1, Book(9255, "21x30", 10), SitePriceCheck.Invalid())
            });

        private static BookGroups Layout(params (long Variant, string Title, int Count, int Spreads)[] groups)
        {
            var result = new List<IReadOnlyList<Book>>();

            foreach (var group in groups)
            {
                result.Add(Enumerable.Range(0, group.Count)
                    .Select(_ => Book(group.Variant, group.Title, group.Spreads))
                    .ToList());
            }

            return BookGrouping.FromManual(SiteRegion.Com, result);
        }

        [Fact]
        public void CountsProjectsBooksAndTheOrderTotal()
        {
            var layout = Layout((9255, "21x30", 6, 10), (9256, "30x30", 4, 10));

            var summary = SubmissionSummary.Build(
                layout,
                new[] { Quote(542m, 542m, 542m, 542m, 542m, 542m), Quote(225m, 225m, 225m, 225m) });

            Assert.Equal(2, summary.ProjectCount);
            Assert.Equal(10, summary.BookCount);
            Assert.Equal(3252m + 900m, summary.Total);
            Assert.True(summary.CanUpload);
            Assert.Empty(summary.Blockers);
        }

        [Fact]
        public void NamesTheFormatAndTheSpreadSpanOfAGroup()
        {
            var layout = Layout((9255, "21x30", 2, 10), (9256, "30x30", 2, 18));

            var summary = SubmissionSummary.Build(layout, new[] { Quote(1m, 1m), Quote(1m, 1m) });

            Assert.Equal("Лайфлат 21x30", summary.Groups[0].FormatName);
            Assert.Equal("Разворотов: 10", summary.Groups[0].PagesText);
            Assert.Equal("Лайфлат 30x30", summary.Groups[1].FormatName);
            Assert.Equal("Разворотов: 18", summary.Groups[1].PagesText);
        }

        [Fact]
        public void ShowsASpanWhenTheBooksOfOneGroupDifferInLength()
        {
            var layout = BookGrouping.FromManual(
                SiteRegion.Com,
                new List<IReadOnlyList<Book>>
                {
                    new List<Book> { Book(9255, "21x30", 10), Book(9255, "21x30", 18) }
                });

            var summary = SubmissionSummary.Build(layout, new[] { Quote(1m, 1m) });

            Assert.Equal("Разворотов: 10–18", Assert.Single(summary.Groups).PagesText);
        }

        [Fact]
        public void RefusesToUploadWhileAGroupHoldsAnImpossibleCombination()
        {
            var layout = BookGrouping.FromManual(
                SiteRegion.Com,
                new List<IReadOnlyList<Book>>
                {
                    new List<Book> { Book(9255, "21x30", 10), Book(9256, "30x30", 10) }
                });

            var summary = SubmissionSummary.Build(layout, new[] { Quote(542m, 542m) });

            Assert.False(summary.CanUpload);
            var blocker = Assert.Single(summary.Blockers);
            Assert.Contains("разных форматов", blocker);
        }

        [Fact]
        public void RefusesToUploadWhenTheSiteWouldNotPriceABook()
        {
            var layout = Layout((9255, "21x30", 1, 10), (9256, "30x30", 1, 10));

            var summary = SubmissionSummary.Build(layout, new[] { Quote(542m), Refused() });

            Assert.False(summary.CanUpload);
            var blocker = Assert.Single(summary.Blockers);
            Assert.Equal("Группа 2: Книга 1: Комбинация недоступна.", blocker);
            Assert.Equal("Цена не получена", summary.Groups[1].PriceText);
        }

        [Fact]
        public void CannotBeSentWhenThereAreNoGroups()
        {
            var layout = BookGrouping.Split(SiteRegion.Com, Array.Empty<Book>(), GroupingMode.InOneProject);

            var summary = SubmissionSummary.Build(layout, Array.Empty<GroupQuote>());

            Assert.Equal(0, summary.ProjectCount);
            Assert.Equal(0, summary.BookCount);
            Assert.Equal(string.Empty, summary.TotalText);
            Assert.False(summary.CanUpload);
            Assert.Contains("В проекте нет ни одной книги.", summary.Blockers);
        }

        [Fact]
        public void NeedsExactlyOneQuotePerGroup()
        {
            var layout = Layout((9255, "21x30", 1, 10), (9256, "30x30", 1, 10));

            Assert.Throws<ArgumentException>(() =>
                SubmissionSummary.Build(layout, new[] { Quote(542m) }));
        }
    }
}
