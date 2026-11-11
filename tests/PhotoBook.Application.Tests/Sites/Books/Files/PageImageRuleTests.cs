using PhotoBook.Application;
using PhotoBook.Application.Sites.Books.Files;
using PhotoBook.Application.Sites.Catalog.Layout;

namespace PhotoBook.Application.Tests.Sites.Books.Files
{
    /// <summary>
    /// The rule a page's file is held to: the site's own size, its own error, and files a few
    /// pixels off reported rather than refused.
    /// </summary>
    public class PageImageRuleTests
    {
        internal const int ResolutionDpi = 300;

        internal static PageImageRule Rule(PixelSize? required = null, int tolerance = 5) =>
            new(required ?? new PixelSize(4913, 3495), tolerance, JpegFiles.Extensions);

        [Fact]
        public void ConvertsTheSitesErrorIntoPixels()
        {
            // 0,423 mm at 300 dpi is the site's five-pixel error; the number is not typed in.
            var rule = PageImageRule.For(new PixelSize(4913, 3495), toleranceMm: 0.423, ResolutionDpi);

            Assert.Equal(5, rule.TolerancePixels);
        }

        [Theory]
        [InlineData(@"C:\photos\01.jpg")]
        [InlineData(@"C:\photos\01.JPG")]
        [InlineData(@"C:\photos\01.jpeg")]
        [InlineData(@"C:\photos\01.JpEg")]
        public void TakesJpegWhateverTheCase(string path)
        {
            Assert.True(Rule().Accepts(path));
        }

        [Theory]
        [InlineData(@"C:\photos\01.png")]
        [InlineData(@"C:\photos\01.tif")]
        [InlineData(@"C:\photos\01")]
        public void RefusesAnythingTheSiteWouldNotTake(string path)
        {
            Assert.False(Rule().Accepts(path));
        }

        [Fact]
        public void TreatsOnlyTheSizeItselfAsExact()
        {
            var rule = Rule();

            Assert.False(rule.IsWithinTolerance(new PixelSize(4913, 3495)));
            Assert.True(rule.IsWithinTolerance(new PixelSize(4915, 3497)));
        }

        [Fact]
        public void CountsTheToleranceOnEachSide()
        {
            var rule = Rule();

            Assert.False(rule.IsTooSmall(new PixelSize(4908, 3490)));
            Assert.True(rule.IsTooSmall(new PixelSize(4907, 3495)));
            Assert.True(rule.IsTooSmall(new PixelSize(4913, 3489)));

            Assert.False(rule.IsTooLarge(new PixelSize(4918, 3500)));
            Assert.True(rule.IsTooLarge(new PixelSize(4919, 3495)));
            Assert.True(rule.IsTooLarge(new PixelSize(4913, 3501)));
        }

        [Fact]
        public void ReportsAFileThatIsBothOverWideAndShort()
        {
            // Nothing stops a file from being too wide and too short at once; both are true.
            var odd = new PixelSize(5000, 3400);

            Assert.True(Rule().IsTooSmall(odd));
            Assert.True(Rule().IsTooLarge(odd));
        }
    }
}
