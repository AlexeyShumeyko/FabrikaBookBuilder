using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Catalog;

namespace PhotoBook.Application.Tests.Sites.Books
{
    /// <summary>
    /// The gate is the last thing between the customer's book and the site, so each way a book
    /// can leave the program wrong is pinned here - above all a book the site prices at zero.
    /// </summary>
    public class BookReadinessTests
    {
        private static IReadOnlyList<PartCheck> Valid() =>
            new List<PartCheck> { PartCheck.Ok() };

        [Fact]
        public void AllowsAFullBookTheSitePriced()
        {
            var readiness = BookReadiness.Check(BookTests.BookWith(10, 10), Valid(), 1410m);

            Assert.True(readiness.IsReady);
            Assert.Empty(readiness.Problems);
        }

        [Fact]
        public void BlocksABookWithoutPhotos()
        {
            var readiness = BookReadiness.Check(BookTests.BookWith(10, 0), Valid(), 1410m);

            var problem = Assert.Single(readiness.Problems);
            Assert.Equal(BookProblemKind.NoFiles, problem.Kind);
            Assert.Equal("В книге нет ни одной фотографии.", problem.Message);
        }

        [Fact]
        public void BlocksABookWithEmptyPages()
        {
            var readiness = BookReadiness.Check(BookTests.BookWith(10, 4), Valid(), 1410m);

            var problem = Assert.Single(readiness.Problems);
            Assert.Equal(BookProblemKind.MissingPages, problem.Kind);
            Assert.Equal("Не хватает фотографий: 6 из 10 страниц пусты.", problem.Message);
        }

        [Fact]
        public void BlocksABookWithFilesBeyondTheLastPage()
        {
            var readiness = BookReadiness.Check(BookTests.BookWith(10, 13), Valid(), 1410m);

            var problem = Assert.Single(readiness.Problems);
            Assert.Equal(BookProblemKind.ExtraFiles, problem.Kind);
            Assert.Equal("Лишних файлов: 3. В книге 10 страниц.", problem.Message);
        }

        [Fact]
        public void BlocksAZeroPriceEvenWhenTheBookIsFull()
        {
            // The price endpoint answers zero with a 200, so this is the shape a set the site
            // will not sell really takes; nothing may reach the order with it.
            var readiness = BookReadiness.Check(BookTests.BookWith(10, 10), Valid(), 0m);

            var problem = Assert.Single(readiness.Problems);
            Assert.Equal(BookProblemKind.ZeroPrice, problem.Kind);
            Assert.False(readiness.IsReady);
        }

        [Fact]
        public void BlocksABookWhosePriceWasNotRead()
        {
            var readiness = BookReadiness.Check(BookTests.BookWith(10, 10), Valid(), null);

            var problem = Assert.Single(readiness.Problems);
            Assert.Equal(BookProblemKind.NoPrice, problem.Kind);
        }

        [Fact]
        public void ReportsSelectionProblemsTogetherWithTheRest()
        {
            var invalid = PartCheck.Invalid(new List<PartProblem>
            {
                new(PartProblemKind.MissingRequired, "Выберите «Плотность разворотов».")
            });

            var readiness = BookReadiness.Check(
                BookTests.BookWith(10, 4),
                new List<PartCheck> { invalid },
                0m);

            var kinds = readiness.Problems.Select(problem => problem.Kind).ToList();
            Assert.Contains(BookProblemKind.InvalidSelection, kinds);
            Assert.Contains(BookProblemKind.MissingPages, kinds);
            Assert.Contains(BookProblemKind.ZeroPrice, kinds);
            Assert.Equal(3, kinds.Count);
            Assert.Contains(
                readiness.Problems,
                problem => problem.Message == "Выберите «Плотность разворотов».");
        }

        [Fact]
        public void UploadSessionCountsWhatIsLeft()
        {
            var session = new UploadSession(
                PhotoBook.Application.Sites.SiteRegion.Com,
                523195,
                FilesTotal: 10,
                FilesUploaded: 7);

            Assert.Equal(3, session.Remaining);
            Assert.False(session.IsComplete);

            var finished = session.WithUploadedFile().WithUploadedFile().WithUploadedFile();
            Assert.True(finished.IsComplete);
            Assert.Equal(0, finished.Remaining);

            // A stray extra acknowledgement must not push the counter past the total.
            Assert.Equal(10, finished.WithUploadedFile().FilesUploaded);
        }
    }
}
