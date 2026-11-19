using System.Collections.Generic;
using PhotoBook.Application.Sites.Books.Files;
using PhotoBook.Application.Sites.Catalog.Layout;

namespace PhotoBook.Application.Tests.Sites.Books.Files
{
    /// <summary>
    /// One order is one book: the group check asks when its books would print at different
    /// sizes, because only the customer can say whether that mix was intended.
    /// </summary>
    public class GroupFileCheckTests
    {
        [Fact]
        public void SaysNothingAboutOneBook()
        {
            var rules = new List<PageImageRule>
            {
                PageImageRuleTests.Rule(new PixelSize(4913, 3495))
            };

            Assert.Empty(GroupFileCheck.Check(rules));
        }

        [Fact]
        public void SaysNothingWhenEveryBookSharesThePageSize()
        {
            var rules = new List<PageImageRule>
            {
                PageImageRuleTests.Rule(new PixelSize(4913, 3495)),
                PageImageRuleTests.Rule(new PixelSize(4913, 3495)),
                PageImageRuleTests.Rule(new PixelSize(4913, 3495))
            };

            Assert.Empty(GroupFileCheck.Check(rules));
        }

        [Fact]
        public void AsksWhenTheGroupHoldsTwoFormats()
        {
            var rules = new List<PageImageRule>
            {
                PageImageRuleTests.Rule(new PixelSize(4913, 3495)),
                PageImageRuleTests.Rule(new PixelSize(5481, 3898)),
                PageImageRuleTests.Rule(new PixelSize(4913, 3495))
            };

            var alert = Assert.Single(GroupFileCheck.Check(rules));

            Assert.Equal(FileAlertLevel.NeedsConfirmation, alert.Level);
            Assert.Null(alert.PageNumber);
            Assert.Contains("4913×3495 (2 кн.)", alert.Message);
            Assert.Contains("5481×3898 (1 кн.)", alert.Message);
        }

        [Fact]
        public void IgnoresBooksThatCarryNoRule()
        {
            var rules = new List<PageImageRule> { PageImageRuleTests.Rule(), null };

            Assert.Empty(GroupFileCheck.Check(rules));
        }
    }
}
