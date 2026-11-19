using PhotoBook.Application.Sites.Catalog.Layout;

namespace PhotoBook.Application.Tests.Sites.Catalog
{
    /// <summary>
    /// The size of a file is measured the way the site measures it. The uploader converts the
    /// page and each margin on its own and adds the results, which is not the same as
    /// converting the sum, and the site refuses a file that is two pixels off.
    /// </summary>
    public class PageGeometryTests
    {
        [Fact]
        public void AddsTheRoundedPiecesInsteadOfRoundingTheSum()
        {
            var page = new PageSize(428, 296);
            var bleeds = new PrintMargins(18, 17, 18, 17);

            var required = PageGeometry.Required(page, bleeds, 300);

            Assert.Equal(new PixelSize(5481, 3898), required);

            // The same 464 mm converted in one step is 5480, one pixel short of what the
            // site's cover upload accepted.
            Assert.Equal(5480, PageGeometry.ToPixels(464, 300));
        }

        [Fact]
        public void RoundsHalfAwayFromZeroLikeTheSite()
        {
            // 3 mm at 300 dpi is 35.43 and 17 mm is 200.79: the site's cover file is 3898
            // pixels tall, which is 3496 + 201 + 201.
            Assert.Equal(35, PageGeometry.ToPixels(3, 300));
            Assert.Equal(201, PageGeometry.ToPixels(17, 300));
            Assert.Equal(3496, PageGeometry.ToPixels(296, 300));
        }
    }
}
