using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Catalog;

namespace PhotoBook.Application.Tests.Sites.Books
{
    /// <summary>
    /// A book is allowed to disagree with its own page count: empty, half-filled and overfull
    /// are everyday states while the customer works, and the model keeps them as they are.
    /// </summary>
    public class BookTests
    {
        private const long Spreads = 94;

        internal static Book BookWith(int pageSlots, int files) =>
            new(
                new SiteProduct(
                    4494,
                    "layflat",
                    "Лайфлат",
                    "photo-book",
                    new List<ProductVariant>()),
                new ProductVariant(37556, "21x30", new List<ProductOption>(), new List<ProductPart>()),
                Spreads,
                new List<SelectedPart>
                {
                    new(Spreads, new PartSelection(new Dictionary<long, long>(), pageSlots))
                },
                Enumerable.Range(1, files)
                    .Select(number => new PageFile($@"C:\photos\{number}.jpg", 1024))
                    .ToList());

        [Fact]
        public void CountsAFullBookAsNeitherShortNorOverfull()
        {
            var book = BookWith(pageSlots: 10, files: 10);

            Assert.Equal(10, book.PageSlots);
            Assert.Equal(0, book.MissingPages);
            Assert.Equal(0, book.ExtraFiles);
        }

        [Fact]
        public void KeepsABookWithoutPhotosAsAValidState()
        {
            var book = BookWith(pageSlots: 10, files: 0);

            Assert.Equal(0, book.FileCount);
            Assert.Equal(10, book.MissingPages);
            Assert.Equal(0, book.ExtraFiles);
        }

        [Fact]
        public void CountsPagesThatAreStillEmpty()
        {
            var book = BookWith(pageSlots: 12, files: 5);

            Assert.Equal(5, book.FileCount);
            Assert.Equal(7, book.MissingPages);
        }

        [Fact]
        public void CountsFilesBeyondTheLastPage()
        {
            var book = BookWith(pageSlots: 10, files: 13);

            Assert.Equal(13, book.FileCount);
            Assert.Equal(0, book.MissingPages);
            Assert.Equal(3, book.ExtraFiles);
        }

        [Fact]
        public void ReadsThePageCountFromThePagePart()
        {
            var book = BookWith(pageSlots: 18, files: 0);

            Assert.Equal(18, book.PagePart?.Selection.Quantity);
        }

        [Fact]
        public void OrderGroupKeepsItsRegionAndBooks()
        {
            var group = new OrderGroup(
                SiteRegion.Com,
                new List<Book> { BookWith(pageSlots: 10, files: 10), BookWith(pageSlots: 10, files: 0) });

            Assert.Equal(SiteRegion.Com, group.Region);
            Assert.Equal(2, group.BookCount);
        }
    }
}
