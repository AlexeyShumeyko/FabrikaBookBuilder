using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Application.Sites.Catalog.Layout;
using PhotoBook.Infrastructure.Sites.Catalog;

namespace PhotoBook.Application.Tests.Sites.Catalog
{
    /// <summary>
    /// The geometry is read from the uploader's own document. The shape below is a trimmed
    /// copy of what the live site returned for the layflat 21x30 on 2026-11-10: the cover is
    /// sized by a table of indexes, the spreads by one size for the whole run, and the index
    /// depends on how thick the spine is.
    /// </summary>
    public class SiteLayoutParserTests
    {
        private const string Layflat = """
        {
          "integration": {
            "id": 37556,
            "settings": { "outputResolution": "300" },
            "elements": [
              {
                "id": 26121,
                "title": "Обложка",
                "settings": {
                  "productPartId": "93",
                  "pageSizeRangesFlag": "1",
                  "pageSizeProductAttributeId": "26",
                  "pageSizeRanges": [
                    { "rangeMin": "8", "rangeMax": "8", "pageSizeWidth": "428", "pageSizeHeight": "296",
                      "pageSizeError": "5",
                      "bleeds": { "left": "18", "top": "17", "right": "18", "bottom": "17" },
                      "safety": { "left": "3", "top": "3", "right": "3", "bottom": "3" } },
                    { "rangeMin": "10", "rangeMax": "10", "pageSizeWidth": "430", "pageSizeHeight": "296",
                      "pageSizeError": "5",
                      "bleeds": { "left": "18", "top": "17", "right": "18", "bottom": "17" },
                      "safety": { "left": "3", "top": "3", "right": "3", "bottom": "3" } },
                    { "rangeMin": "12", "rangeMax": "12", "pageSizeWidth": "432", "pageSizeHeight": "296",
                      "pageSizeError": "5",
                      "bleeds": { "left": "18", "top": "17", "right": "18", "bottom": "17" },
                      "safety": { "left": "3", "top": "3", "right": "3", "bottom": "3" } },
                    { "rangeMin": "14", "rangeMax": "14", "pageSizeWidth": "434", "pageSizeHeight": "296",
                      "pageSizeError": "5",
                      "bleeds": { "left": "18", "top": "17", "right": "18", "bottom": "17" },
                      "safety": { "left": "3", "top": "3", "right": "3", "bottom": "3" } },
                    { "rangeMin": "16", "rangeMax": "16", "pageSizeWidth": "436", "pageSizeHeight": "296",
                      "pageSizeError": "5",
                      "bleeds": { "left": "18", "top": "17", "right": "18", "bottom": "17" },
                      "safety": { "left": "3", "top": "3", "right": "3", "bottom": "3" } },
                    { "rangeMin": "18", "rangeMax": "18", "pageSizeWidth": "438", "pageSizeHeight": "296",
                      "pageSizeError": "5",
                      "bleeds": { "left": "18", "top": "17", "right": "18", "bottom": "17" },
                      "safety": { "left": "3", "top": "3", "right": "3", "bottom": "3" } }
                  ]
                }
              },
              {
                "id": 26129,
                "title": "Разворот",
                "settings": {
                  "productPartId": "94",
                  "imageSizeWay": "manually",
                  "imageSizeWidth": "410",
                  "imageSizeHeight": "290",
                  "imageSizeError": "1",
                  "cuttingLineLeft": "3", "cuttingLineTop": "3",
                  "cuttingLineRight": "3", "cuttingLineBottom": "3",
                  "safetyZoneLeft": "3", "safetyZoneTop": "3",
                  "safetyZoneRight": "3", "safetyZoneBottom": "3"
                }
              }
            ]
          },
          "productId": 4494,
          "productComponentId": 9255,
          "productPartsTitles": { "93": "Обложка", "94": "Разворот" },
          "productAttributes": [
            {
              "id": 26,
              "productSettings": {
                "condition": { "enabled": "1", "optionId": "65740", "optionElementsIds": ["420271"] },
                "dependencies": [
                  {
                    "way": "part_quantity",
                    "partId": 94,
                    "numberValueMode": "from-ranges",
                    "numberValueRanges": [
                      { "from": "2", "to": "7", "value": "8" },
                      { "from": "8", "to": "10", "value": "10" },
                      { "from": "11", "to": "13", "value": "12" },
                      { "from": "14", "to": "16", "value": "14" },
                      { "from": "17", "to": "20", "value": "16" }
                    ]
                  }
                ]
              }
            },
            {
              "id": 26,
              "productSettings": {
                "condition": { "enabled": "1", "optionId": "65740", "optionElementsIds": ["420272"] },
                "dependencies": [
                  {
                    "way": "part_quantity",
                    "partId": 94,
                    "numberValueMode": "from-ranges",
                    "numberValueRanges": [
                      { "from": "2", "to": "4", "value": "8" },
                      { "from": "5", "to": "5", "value": "10" },
                      { "from": "6", "to": "7", "value": "12" },
                      { "from": "8", "to": "9", "value": "14" },
                      { "from": "10", "to": "11", "value": "16" },
                      { "from": "12", "to": "12", "value": "18" }
                    ]
                  }
                ]
              }
            }
          ]
        }
        """;

        private static SiteLayout ParseLayflat()
        {
            var result = new SiteLayoutParser().Parse(Layflat);
            Assert.True(result.IsSuccess, result.Error);
            return result.Value!;
        }

        [Fact]
        public void ReadsTheResolutionAndBothParts()
        {
            var layout = ParseLayflat();

            Assert.Equal(300, layout.ResolutionDpi);
            Assert.Equal(4494, layout.ProductId);
            Assert.Equal(9255, layout.ComponentId);

            // The uploader's integration, which the create-project endpoint needs and which is
            // not the format id: without it the endpoint answers 404 with a model-not-found.
            Assert.Equal(37556, layout.IntegrationId);
            Assert.Equal(2, layout.Parts.Count);

            var cover = layout.Part(93);
            Assert.NotNull(cover);
            Assert.Equal("Обложка", cover!.Title);
            Assert.Equal(6, cover.Sizes.Count);
            Assert.Null(cover.FixedSize);

            var spreads = layout.Part(94);
            Assert.NotNull(spreads);
            Assert.Equal("Разворот", spreads!.Title);
            Assert.Empty(spreads.Sizes);
            Assert.Equal(new PageSize(410, 290), spreads.FixedSize);
        }

        [Fact]
        public void SizesTheCoverAtTheSmallestSpineForTwoSpreads()
        {
            // Two spreads without an interlayer put the cover at index 8: 428 x 296 mm with an
            // 18/17 mm bleed. The site's own cover upload for this book was 5481 x 3898.
            var pixels = ParseLayflat().RequiredPixels(93, 2, "65740", "420271");

            Assert.Equal(new PixelSize(5481, 3898), pixels);
        }

        [Fact]
        public void WidensTheCoverWhenTheIndexGrows()
        {
            // Eighteen spreads without an interlayer put the cover at index 16: 436 mm, which
            // is 5150 pixels wide plus two bleeds of 213.
            var pixels = ParseLayflat().RequiredPixels(93, 18, "65740", "420271");

            Assert.Equal(new PixelSize(5576, 3898), pixels);
        }

        [Fact]
        public void SizesTheSpreadsOnlyOnceForTheWholeRun()
        {
            var layout = ParseLayflat();

            Assert.Equal(new PixelSize(4913, 3495), layout.RequiredPixels(94, 2, "65740", "420271"));
            Assert.Equal(new PixelSize(4913, 3495), layout.RequiredPixels(94, 18, "65740", "420271"));
        }

        [Fact]
        public void ReturnsNoSizeForACombinationTheSiteDidNotDescribe()
        {
            var layout = ParseLayflat();

            // Cardboard of 1 mm runs to twelve spreads; beyond that the site offers no index,
            // so there is no size to check a file against and the answer is "not said".
            Assert.Null(layout.RequiredPixels(93, 18, "65740", "420272"));
            Assert.Null(layout.RequiredPixels(93, 2, "65740", "999999"));
            Assert.Null(layout.RequiredPixels(93, 2, null, null));
            Assert.Null(layout.RequiredPixels(95, 2, "65740", "420271"));
        }

        [Fact]
        public void RefusesADocumentWithoutTheUploader()
        {
            var result = new SiteLayoutParser().Parse("""{ "productId": 4494 }""");

            Assert.False(result.IsSuccess);
            Assert.Contains("загрузчика", result.Error);
        }

        [Fact]
        public void RefusesADocumentWithoutResolution()
        {
            var result = new SiteLayoutParser().Parse("""
            {
              "integration": { "settings": { "outputResolution": "" }, "elements": [] },
              "productId": 4494
            }
            """);

            Assert.False(result.IsSuccess);
            Assert.Contains("разрешение", result.Error);
        }

        [Fact]
        public void RefusesMalformedJson()
        {
            var result = new SiteLayoutParser().Parse("{ \"integration\": ");

            Assert.False(result.IsSuccess);
            Assert.Contains("неизвестном формате", result.Error);
        }
    }
}
