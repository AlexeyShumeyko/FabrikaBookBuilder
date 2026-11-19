using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Pricing;
using PhotoBook.Application.Sites.Catalog;

namespace PhotoBook.Application.Tests.Sites.Books.Pricing
{
    /// <summary>
    /// The query is the same one the calculator page sends, so what it carries is pinned: the
    /// price endpoint answered zero for every combination until the shape matched.
    /// </summary>
    public class BookPriceQueryTests
    {
        internal static Book Book(params SelectedPart[] parts) =>
            new(
                new SiteProduct(
                    4494,
                    "layflat",
                    "Лайфлат",
                    "photo-book",
                    new List<ProductVariant>()),
                new ProductVariant(9255, "21x30", new List<ProductOption>(), new List<ProductPart>()),
                PagePartId: 94,
                parts,
                new List<PageFile>());

        internal static SelectedPart Part(long partId, int quantity, params (long Option, long Element)[] chosen) =>
            new(partId, new PartSelection(
                new Dictionary<long, long>(chosen.ToDictionary(pair => pair.Option, pair => pair.Element)),
                quantity));

        [Fact]
        public void SendsTheProductTheFormatAndEveryChoice()
        {
            var book = Book(
                Part(93, 1, (65075, 417892)),
                Part(94, 10, (65738, 420268)));

            var query = BookPriceQuery.From(book);

            Assert.Equal(4494, query.ProductId);
            Assert.Equal(9255, query.ProductComponentId);
            Assert.Equal(417892, query.Options[65075]);
            Assert.Equal(420268, query.Options[65738]);
            Assert.Equal(1, query.PartQuantities[93]);
            Assert.Equal(10, query.PartQuantities[94]);
        }

        [Fact]
        public void LeavesOutAPartWithNoQuantity()
        {
            var book = Book(Part(93, 0, (65075, 417892)), Part(94, 10));

            var query = BookPriceQuery.From(book);

            Assert.False(query.PartQuantities.ContainsKey(93));
            Assert.Equal(10, query.PartQuantities[94]);
            Assert.Equal(417892, query.Options[65075]);
        }

        [Fact]
        public void KeepsTheFirstChoiceOfAnOptionTwoPartsShare()
        {
            // One option, one answer: the site is asked once per option, not once per part.
            var book = Book(
                Part(93, 1, (70519, 450642)),
                Part(94, 10, (70519, 999999)));

            var query = BookPriceQuery.From(book);

            Assert.Equal(450642, query.Options[70519]);
        }

        [Fact]
        public void SendsNothingWhenTheBookHasNoChoicesYet()
        {
            var query = BookPriceQuery.From(Book());

            Assert.Empty(query.Options);
            Assert.Empty(query.PartQuantities);
        }
    }
}
