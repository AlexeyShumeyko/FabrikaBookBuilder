using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites.Books.Files;
using PhotoBook.Application.Sites.Catalog.Layout;

namespace PhotoBook.Application.Tests.Sites.Books.Files
{
    /// <summary>
    /// The file check is what the preparation screen explains a refusal with, so each level is
    /// pinned here, including the case the roadmap names: 5000×3500 against 4913×3495.
    /// </summary>
    public class BookFileCheckTests
    {
        private static PageFileFacts Page(string name, PixelSize? size) =>
            new($@"C:\photos\{name}", Exists: true, size);

        private static PageFileFacts Missing(string name) =>
            new($@"C:\photos\{name}", Exists: false, null);

        [Fact]
        public void SaysNothingAboutFilesThatFitThePage()
        {
            var pages = new[] { Page("1.jpg", new PixelSize(4913, 3495)) };

            var alerts = BookFileCheck.Check(pages, PageImageRuleTests.Rule());

            Assert.Empty(alerts);
        }

        [Fact]
        public void ReportsAWiderFileAsAQuestionRatherThanARefusal()
        {
            // The roadmap's own case: 87 px wider and 5 px taller than the page.
            var pages = new[]
            {
                Page("1.jpg", new PixelSize(4913, 3495)),
                Page("2.jpg", new PixelSize(4913, 3495)),
                Page("3.jpg", new PixelSize(5000, 3500))
            };

            var alert = Assert.Single(BookFileCheck.Check(pages, PageImageRuleTests.Rule()));

            Assert.Equal(FileAlertLevel.NeedsConfirmation, alert.Level);
            Assert.Equal(3, alert.PageNumber);
            Assert.Contains("5000×3500", alert.Message);
            Assert.Contains("4913×3495", alert.Message);
        }

        [Fact]
        public void BlocksAFileSmallerThanThePage()
        {
            var pages = new[] { Page("1.jpg", new PixelSize(4000, 3495)) };

            var alert = Assert.Single(BookFileCheck.Check(pages, PageImageRuleTests.Rule()));

            Assert.Equal(FileAlertLevel.Blocking, alert.Level);
            Assert.Contains("4000×3495", alert.Message);
            Assert.Contains("не примет", alert.Message);
        }

        [Fact]
        public void ReportsAPixelOffAsInformation()
        {
            var pages = new[] { Page("1.jpg", new PixelSize(4915, 3497)) };

            var alert = Assert.Single(BookFileCheck.Check(pages, PageImageRuleTests.Rule()));

            Assert.Equal(FileAlertLevel.Information, alert.Level);
            Assert.Contains("±5 px", alert.Message);
        }

        [Fact]
        public void BlocksAFileThatIsNoLongerThere()
        {
            var pages = new[] { Missing("1.jpg") };

            var alert = Assert.Single(BookFileCheck.Check(pages, PageImageRuleTests.Rule()));

            Assert.Equal(FileAlertLevel.Blocking, alert.Level);
            Assert.Contains("«1.jpg» не найден", alert.Message);
        }

        [Fact]
        public void BlocksAFileThatIsThereButUnreadable()
        {
            var pages = new[] { Page("1.jpg", null) };

            var alert = Assert.Single(BookFileCheck.Check(pages, PageImageRuleTests.Rule()));

            Assert.Equal(FileAlertLevel.Blocking, alert.Level);
            Assert.Contains("не читается", alert.Message);
        }

        [Fact]
        public void BlocksACoverTheSiteWouldNotTake()
        {
            var pages = new[] { Page("scan.png", new PixelSize(5481, 3898)) };

            var alert = Assert.Single(BookFileCheck.Check(pages, PageImageRuleTests.Rule()));

            Assert.Equal(FileAlertLevel.Blocking, alert.Level);
            Assert.Contains("только JPEG", alert.Message);
        }

        [Fact]
        public void NumbersThePagesFromOneAndKeepsEveryFile()
        {
            var pages = new List<PageFileFacts>
            {
                Page("1.jpg", new PixelSize(4913, 3495)),
                Page("2.png", new PixelSize(4913, 3495)),
                Missing("3.jpg")
            };

            var alerts = BookFileCheck.Check(pages, PageImageRuleTests.Rule());

            Assert.Equal(2, alerts.Count);
            Assert.Equal(new int?[] { 2, 3 }, alerts.Select(alert => alert.PageNumber).ToList());
        }
    }
}
