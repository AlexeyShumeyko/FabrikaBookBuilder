using System.Collections.Generic;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Files;
using PhotoBook.Application.Sites.Catalog;
using Xunit;

namespace PhotoBook.Application.Tests.Sites.Books.Files
{
    /// <summary>
    /// One book, one word: the list on the upload screen says what stands between a book and the
    /// site, and the check that looked at its files is what decides it.
    /// </summary>
    public class BookStatusTests
    {
        private const long PagePart = 94;
        private const long CoverPart = 93;

        [Fact]
        public void AFormatThatNeedsACoverAndHasNoneIsSaidSo()
        {
            var book = Book(2, coverPart: CoverPart, coverFile: null);

            var status = BookStatus.Of(book, null, isChecking: false);

            Assert.Equal(BookStatusKind.NoCover, status.Kind);
            Assert.Equal("Нет обложки", status.Text);
            Assert.True(status.HasProblem);
        }

        [Fact]
        public void ABookShorterThanItsPartIsSaidSo()
        {
            var book = Book(files: 5, spreads: 8);

            var status = BookStatus.Of(book, null, isChecking: false);

            Assert.Equal(BookStatusKind.MissingSpreads, status.Kind);
            Assert.Equal("Не хватает разворотов", status.Text);
            Assert.Contains(status.Reasons, reason => reason.Contains("3 из 8"));
        }

        /// <summary>
        /// What the site said about one file belongs to that file's row, not to the book's own
        /// reasons: the expansion under the book lists every file with its complaint, and a
        /// second copy above it was the same text twice, all of it red.
        /// </summary>
        [Fact]
        public void AFileTheSiteWouldRefuseStopsTheBookWithoutRepeatingItsSentence()
        {
            var book = Book(files: 2);
            var check = Checked(book, new FileAlert(FileAlertLevel.Blocking, 1, "Разворот 1: 800×600 меньше 410×290 мм."));

            var status = BookStatus.Of(book, check, isChecking: false);

            Assert.Equal(BookStatusKind.FilesFailed, status.Kind);
            Assert.Equal("Ошибка в файлах", status.Text);
            Assert.True(status.HasProblem);
            Assert.Empty(status.Reasons);
        }

        /// <summary>
        /// A file the site may trim is not a refusal: the customer answers it on the screen, and
        /// the book stays ready. The question is in the file's own row.
        /// </summary>
        [Fact]
        public void AFileTheSiteMayTrimIsStillReady()
        {
            var book = Book(files: 2);
            var check = Checked(book, new FileAlert(FileAlertLevel.NeedsConfirmation, 1, "Разворот 1: край обрежется."));

            var status = BookStatus.Of(book, check, isChecking: false);

            Assert.Equal(BookStatusKind.Ready, status.Kind);
            Assert.True(status.IsReady);
            Assert.False(status.HasProblem);
            Assert.Empty(status.Reasons);
        }

        [Fact]
        public void ABookWhoseFilesWereNotReadYetIsBeingChecked()
        {
            var status = BookStatus.Of(Book(files: 2), null, isChecking: false);

            Assert.Equal(BookStatusKind.Checking, status.Kind);
            Assert.Equal("Проверяется", status.Text);
            Assert.False(status.HasProblem);
        }

        private static Book Book(int files, int spreads = 2, long coverPart = 0, PageFile? coverFile = null) =>
            new(
                new SiteProduct(4494, "layflat", "Лайфлат", "photo-book", new List<ProductVariant>()),
                new ProductVariant(9255, "21x30", new List<ProductOption>(), new List<ProductPart>()),
                PagePartId: PagePart,
                new List<SelectedPart>
                {
                    new(PagePart, new PartSelection(new Dictionary<long, long>(), spreads))
                },
                Pages(files),
                CoverPartId: coverPart,
                CoverFile: coverFile);

        private static List<PageFile> Pages(int count)
        {
            var pages = new List<PageFile>(count);
            for (var index = 1; index <= count; index++)
                pages.Add(new PageFile(@"C:\photos\" + index + ".jpg", 1024));

            return pages;
        }

        private static CheckedBook Checked(Book book, params FileAlert[] alerts) =>
            new(1, "Книга 1", null, alerts);
    }
}
