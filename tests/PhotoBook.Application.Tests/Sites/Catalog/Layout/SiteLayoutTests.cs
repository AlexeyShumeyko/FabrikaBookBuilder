using System;
using System.Collections.Generic;
using PhotoBook.Application.Sites.Catalog.Layout;

namespace PhotoBook.Application.Tests.Sites.Catalog.Layout
{
    /// <summary>
    /// A book names its page by what it chose, not by an option the program picked: the cover
    /// grows with the interlayer, and only the selection says which table that is.
    /// </summary>
    public class SiteLayoutTests
    {
        [Fact]
        public void SizesAPageByTheChoiceTheBookMade()
        {
            var layout = Layout();

            var plain = layout.RequiredPixels(93, 10, new Dictionary<long, long> { [26] = 4 });
            var cardboard = layout.RequiredPixels(93, 10, new Dictionary<long, long> { [26] = 5 });

            // Cardboard makes the spine thicker, so the cover it needs is wider at the same
            // number of spreads - the quantity alone would have chosen the same table twice.
            Assert.NotNull(plain);
            Assert.NotNull(cardboard);
            Assert.True(cardboard!.Width > plain!.Width);
            Assert.Equal(plain.Height, cardboard.Height);
        }

        [Fact]
        public void SaysNothingWhenNeitherTheChoiceNorThePartNamesASize()
        {
            var layout = Layout();

            Assert.Null(layout.RequiredPixels(93, 10, new Dictionary<long, long> { [26] = 99 }));
        }

        [Fact]
        public void FallsBackToTheSingleSizeWhenNoChoiceSizesThePart()
        {
            var layout = Layout();

            var size = layout.RequiredPixels(94, 10, new Dictionary<long, long> { [26] = 4 });

            Assert.Equal(PageGeometry.Required(new PageSize(410, 290), PrintMargins.None, 300), size);
        }

        /// <summary>A cover indexed by the interlayer and a spread sized once for the run.</summary>
        private static SiteLayout Layout()
        {
            var bleeds = new PrintMargins(18, 17, 18, 17);
            var safety = new PrintMargins(3, 3, 3, 3);

            var cover = new PartLayout(
                93,
                "Обложка",
                new[]
                {
                    new PageSizeRange(8, new PageSize(428, 296), bleeds),
                    new PageSizeRange(10, new PageSize(430, 296), bleeds),
                    new PageSizeRange(12, new PageSize(432, 296), bleeds)
                },
                null,
                bleeds,
                new[]
                {
                    new SizeIndexCondition("26", "4", new[] { new SizeIndexRule(8, 10, 10) }),
                    new SizeIndexCondition("26", "5", new[] { new SizeIndexRule(8, 10, 12) })
                },
                safety,
                5);

            var spread = new PartLayout(
                94,
                "Разворот",
                Array.Empty<PageSizeRange>(),
                new PageSize(410, 290),
                PrintMargins.None,
                Array.Empty<SizeIndexCondition>(),
                PrintMargins.None,
                0);

            return new SiteLayout(4494, 9255, 300, new[] { cover, spread }, 37556);
        }
    }
}
