using System;
using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Order;
using PhotoBook.Application.Sites.Books.Pricing;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Application.Sites.Pricing;
using PhotoBook.Application.Sites.Upload;

namespace PhotoBook.Application.Tests.Sites.Upload
{
    /// <summary>
    /// The upload sends one project per book, with the ids the site needs and the price already
    /// obtained. A book that cannot go is named rather than guessed at, because a project created
    /// without its price or its integration is one the customer never confirmed.
    /// </summary>
    public class UploadPlanTests
    {
        private const long Product = 4494;
        private const long Layflat = 9255;
        private const long Life = 9257;
        private const long Integration = 37556;

        [Fact]
        public void MakesOneProjectPerBookWithItsOwnFormatAndPrice()
        {
            var first = Book(Layflat, "21x30");
            var second = Book(Life, "20x20");

            var plan = UploadPlans.Build(
                SiteRegion.Com,
                new OrderDraft(new[] { first, second }, Array.Empty<string>()),
                Quote(first, second),
                Formats());

            Assert.True(plan.IsComplete);
            Assert.Empty(plan.Problems);
            Assert.Equal(2, plan.Groups.Count);

            Assert.Equal(1, plan.Groups[0].Number);
            Assert.Equal(Product, plan.Groups[0].Request.ProductId);
            Assert.Equal(Layflat, plan.Groups[0].Request.ProductComponentId);
            Assert.Equal(Integration, plan.Groups[0].Request.IntegrationId);
            Assert.Equal(first.Files, plan.Groups[0].Pages);

            Assert.Equal(Life, plan.Groups[1].Request.ProductComponentId);
            Assert.Equal(2, plan.Summary.ProjectCount);
            Assert.Equal(2, plan.Summary.BookCount);
        }

        [Fact]
        public void SendsTheCoverBeforeTheSpreads()
        {
            var book = Book(Layflat, "21x30") with
            {
                CoverPartId = 93,
                CoverFile = new PageFile(@"C:\photos\cover.jpg", 1024),
            };

            var plan = UploadPlans.Build(
                SiteRegion.Com,
                new OrderDraft(new[] { book }, Array.Empty<string>()),
                Quote(book),
                Formats());

            var pages = Assert.Single(plan.Groups).Pages;
            Assert.EndsWith("cover.jpg", pages[0].Path, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(book.FileCount + 1, pages.Count);
            Assert.Equal("/photos/1.jpg", pages[1].Path);
        }

        [Fact]
        public void LeavesOutABookWhoseFormatTheSiteDidNotGiveAnIntegration()
        {
            var known = Book(Layflat, "21x30");
            var unknown = Book(9999, "30x30");

            var plan = UploadPlans.Build(
                SiteRegion.Com,
                new OrderDraft(new[] { known, unknown }, Array.Empty<string>()),
                Quote(known, unknown),
                Formats());

            Assert.False(plan.IsComplete);
            Assert.Single(plan.Groups);
            Assert.Contains(plan.Problems, problem => problem.Contains("интеграцию"));
        }

        [Fact]
        public void NamesABookTheSiteDidNotPrice()
        {
            var priced = Book(Layflat, "21x30");
            var unpriced = Book(Life, "20x20");

            var plan = UploadPlans.Build(
                SiteRegion.Com,
                new OrderDraft(new[] { priced, unpriced }, Array.Empty<string>()),
                Quote(priced),
                Formats());

            Assert.False(plan.IsComplete);
            Assert.Single(plan.Groups);
            Assert.Contains(plan.Problems, problem => problem.Contains("цена не получена"));
        }

        [Fact]
        public void RefusesAnOrderThatWasNeverAssembled()
        {
            var plan = UploadPlans.Build(SiteRegion.Com, null, null, Formats());

            Assert.False(plan.IsComplete);
            Assert.Empty(plan.Groups);
            Assert.Single(plan.Problems);
        }

        private static Book Book(long variantId, string title) =>
            new(
                new SiteProduct(Product, "layflat", "Лайфлат", "photo-book", new List<ProductVariant>()),
                new ProductVariant(variantId, title, new List<ProductOption>(), new List<ProductPart>()),
                PagePartId: 94,
                new List<SelectedPart>
                {
                    new(94, new PartSelection(new Dictionary<long, long>(), 10))
                },
                new List<PageFile> { new("/photos/1.jpg", 1024) });

        private static GroupQuote Quote(params Book[] books) =>
            new(books
                .Select((book, index) => new BookQuote(
                    index + 1,
                    book,
                    SitePriceCheck.Valid(new SitePriceQuote(1024, "1 024,00 RUB", "RUB"))))
                .ToList());

        /// <summary>What the catalog returns for the two formats that have an integration.</summary>
        private static IReadOnlyList<SiteProduct> Formats() => new[]
        {
            new SiteProduct(
                Product,
                "layflat",
                "Лайфлат",
                "photo-book",
                new List<ProductVariant>
                {
                    new(Layflat, "21x30", new List<ProductOption>(), new List<ProductPart>()),
                    new(Life, "20x20", new List<ProductOption>(), new List<ProductPart>()),
                },
                Integration),
        };
    }
}
