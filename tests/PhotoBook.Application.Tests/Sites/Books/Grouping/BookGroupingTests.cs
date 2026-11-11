using System;
using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Grouping;
using PhotoBook.Application.Sites.Catalog;

namespace PhotoBook.Application.Tests.Sites.Books.Grouping
{
    /// <summary>
    /// The roadmap's own criterion lives here: ten books in two formats become two groups and
    /// never one, whatever the customer chose about the rest.
    /// </summary>
    public class BookGroupingTests
    {
        private static Book Book(long variantId, string variantTitle, string name = "Лайфлат") =>
            new(
                new SiteProduct(4494, "layflat", name, "photo-book", new List<ProductVariant>()),
                new ProductVariant(variantId, variantTitle, new List<ProductOption>(), new List<ProductPart>()),
                PagePartId: 94,
                new List<SelectedPart> { new(94, new PartSelection(new Dictionary<long, long>(), 10)) },
                new List<PageFile>());

        private static List<Book> TenBooks() =>
            Enumerable.Range(0, 10)
                .Select(index => index < 6
                    ? Book(9255, "21x30")
                    : Book(9256, "30x30"))
                .ToList();

        [Fact]
        public void PutsBooksOfOneFormatIntoOneProject()
        {
            var books = new[] { Book(9255, "21x30"), Book(9255, "21x30"), Book(9255, "21x30") };

            var layout = BookGrouping.Split(SiteRegion.Com, books, GroupingMode.InOneProject);

            var group = Assert.Single(layout.Groups);
            Assert.Equal(3, group.BookCount);
            Assert.Equal(SiteRegion.Com, group.Region);
            Assert.True(layout.IsUsable);
        }

        [Fact]
        public void SeparatesTwoFormatsEvenWhenAskedForOneProject()
        {
            var layout = BookGrouping.Split(SiteRegion.Com, TenBooks(), GroupingMode.InOneProject);

            Assert.Equal(2, layout.Groups.Count);
            Assert.Equal(new[] { 6, 4 }, layout.Groups.Select(group => group.BookCount));
            Assert.Empty(layout.Problems);

            foreach (var group in layout.Groups)
                Assert.Single(group.Books.Select(book => book.Variant.Id).Distinct());
        }

        [Fact]
        public void GivesEveryBookItsOwnProjectWhenAsked()
        {
            var layout = BookGrouping.Split(SiteRegion.Com, TenBooks(), GroupingMode.EachBookSeparate);

            Assert.Equal(10, layout.Groups.Count);
            Assert.All(layout.Groups, group => Assert.Equal(1, group.BookCount));
            Assert.Equal(10, layout.BookCount);
        }

        [Fact]
        public void KeepsTheBooksInTheProjectsOrder()
        {
            var books = new[] { Book(9255, "21x30"), Book(9256, "30x30"), Book(9255, "21x30") };

            var layout = BookGrouping.Split(SiteRegion.Com, books, GroupingMode.InOneProject);

            Assert.Same(books[0], layout.Groups[0].Books[0]);
            Assert.Same(books[2], layout.Groups[0].Books[1]);
            Assert.Same(books[1], layout.Groups[1].Books[0]);
        }

        [Fact]
        public void ReportsAManualGroupThatHoldsTwoFormats()
        {
            var layout = BookGrouping.FromManual(
                SiteRegion.By,
                new List<IReadOnlyList<Book>>
                {
                    new List<Book> { Book(9255, "21x30"), Book(9256, "30x30") }
                });

            var problem = Assert.Single(layout.Problems);
            Assert.Contains("разных форматов", problem);
            Assert.Contains("«Лайфлат 21x30»", problem);
            Assert.Contains("«Лайфлат 30x30»", problem);
            Assert.False(layout.IsUsable);
            Assert.Single(layout.Groups);
        }

        [Fact]
        public void KeepsTwoManualGroupsOfTheSameFormatApart()
        {
            var layout = BookGrouping.FromManual(
                SiteRegion.Com,
                new List<IReadOnlyList<Book>>
                {
                    new List<Book> { Book(9255, "21x30") },
                    new List<Book> { Book(9255, "21x30") }
                });

            Assert.Equal(2, layout.Groups.Count);
            Assert.Empty(layout.Problems);
            Assert.True(layout.IsUsable);
        }

        [Fact]
        public void ReportsABookThatEndedUpInTwoGroups()
        {
            var book = Book(9255, "21x30");

            var layout = BookGrouping.FromManual(
                SiteRegion.Com,
                new List<IReadOnlyList<Book>>
                {
                    new List<Book> { book },
                    new List<Book> { book }
                });

            var problem = Assert.Single(layout.Problems);
            Assert.Contains("уже есть в другой группе", problem);
        }

        [Fact]
        public void ReportsAnEmptyManualGroup()
        {
            var layout = BookGrouping.FromManual(
                SiteRegion.Com,
                new List<IReadOnlyList<Book>>
                {
                    new List<Book> { Book(9255, "21x30") },
                    Array.Empty<Book>()
                });

            var problem = Assert.Single(layout.Problems);
            Assert.Contains("Группа 2 пуста", problem);
            Assert.Single(layout.Groups);
        }

        [Fact]
        public void SaysAProjectWithoutBooksIsNotAUsableLayout()
        {
            var layout = BookGrouping.Split(SiteRegion.Com, Array.Empty<Book>(), GroupingMode.InOneProject);

            Assert.Empty(layout.Groups);
            Assert.Equal(new[] { "В проекте нет ни одной книги." }, layout.Problems);
            Assert.False(layout.IsUsable);
        }

        [Fact]
        public void RefusesToGuessAManualLayout()
        {
            Assert.Throws<ArgumentException>(() =>
                BookGrouping.Split(SiteRegion.Com, TenBooks(), GroupingMode.Manual));
        }
    }
}
