using System.Linq;
using PhotoBook.Application.Sites.Books;
using CoreBook = PhotoBook.Core.Book;
using CorePage = PhotoBook.Core.Page;
using PhotoBook.Core;

namespace PhotoBook.Application.Tests.Sites
{
    /// <summary>
    /// The preparation list is what shows a book before it is sent, and its job is to be honest
    /// about a book that cannot go yet.
    /// </summary>
    public class BookInventoryTests
    {
        [Fact]
        public void ListsABookWithoutPhotosSoItCannotBeSent()
        {
            var project = WithBooks(Book(spreads: 2, filledSpreads: 0, cover: false));

            var book = Assert.Single(BookInventory.From(project).Books);

            Assert.Equal("Книга 1", book.Name);
            Assert.Equal(1, book.Number);
            Assert.Equal(2, book.Spreads);
            Assert.Equal(0, book.Photos);
            Assert.False(book.HasCover);
            Assert.Equal(BookCondition.NoPhotos, book.Condition);
            Assert.False(book.CanUpload);
            Assert.False(BookInventory.From(project).AllReady);
        }

        [Fact]
        public void MarksAHalfFilledBookAsIncomplete()
        {
            var project = WithBooks(Book(spreads: 2, filledSpreads: 1));

            var book = Assert.Single(BookInventory.From(project).Books);

            Assert.Equal(2, book.Photos);
            Assert.Equal(3, book.Slots);
            Assert.Equal(BookCondition.Incomplete, book.Condition);
            Assert.False(book.CanUpload);
        }

        [Fact]
        public void OnlyAFullBookIsReady()
        {
            var project = WithBooks(Book(spreads: 2, filledSpreads: 2));

            var inventory = BookInventory.From(project);
            var book = Assert.Single(inventory.Books);

            Assert.Equal(3, book.Photos);
            Assert.Equal(BookCondition.Ready, book.Condition);
            Assert.True(book.CanUpload);
            Assert.True(inventory.AllReady);
        }

        [Fact]
        public void KeepsTheProjectOrderAndTheNamesItHas()
        {
            var first = Book(spreads: 1, filledSpreads: 1, name: "  Лето  ");
            first.BookIndex = 0;
            var second = Book(spreads: 1, filledSpreads: 1);
            second.BookIndex = 1;

            var books = BookInventory.From(WithBooks(first, second)).Books;

            Assert.Equal(new[] { "Лето", "Книга 2" }, books.Select(book => book.Name));
            Assert.Equal(new[] { 1, 2 }, books.Select(book => book.Number));
        }

        [Fact]
        public void CountsPhotographsTheProjectNeverPlaced()
        {
            var placed = "/photos/one.jpg";
            var project = WithBooks(Book(spreads: 1, filledSpreads: 1, coverPath: placed, coverOnly: true));
            project.AvailableFiles.Add(placed);
            project.AvailableFiles.Add("/photos/two.jpg");
            project.AvailableFiles.Add("/PHOTOS/Two.jpg");

            Assert.Equal(1, BookInventory.From(project).LeftoverPhotos);
        }

        [Fact]
        public void AnEmptyProjectHasNothingToSend()
        {
            var inventory = BookInventory.From(new Project());

            Assert.Empty(inventory.Books);
            Assert.False(inventory.AllReady);
            Assert.Equal(0, inventory.LeftoverPhotos);
        }

        private static Project WithBooks(params CoreBook[] books)
        {
            var project = new Project();
            foreach (var book in books)
                project.Books.Add(book);

            return project;
        }

        /// <summary>
        /// A book with the given number of spreads, the first of them filled in, and a cover that
        /// can be filled or left empty on its own.
        /// </summary>
        private static CoreBook Book(
            int spreads,
            int filledSpreads,
            string? name = null,
            bool cover = true,
            string? coverPath = null,
            bool coverOnly = false)
        {
            var book = new CoreBook { Name = name };
            book.Cover = new CorePage
            {
                IsCover = true,
                SourcePath = cover || coverPath is not null ? coverPath ?? "/photos/cover.jpg" : null
            };

            if (coverOnly)
                return book;

            for (var index = 1; index <= spreads; index++)
            {
                book.Pages.Add(new CorePage
                {
                    Index = index,
                    SourcePath = index <= filledSpreads ? $"/photos/{index}.jpg" : null
                });
            }

            return book;
        }
    }
}
