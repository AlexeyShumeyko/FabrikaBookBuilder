using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Infrastructure.Sites.Catalog;

namespace PhotoBook.Application.Tests.Sites.Catalog
{
    /// <summary>
    /// The catalog is read once and reused: the platform is shared by two sites and load on it is
    /// forbidden, so a stored document must not turn into a request, and a request must not
    /// happen twice for the same product.
    /// </summary>
    public class SiteCatalogServiceTests
    {
        private const string Product =
            "{\"product\":{\"id\":4494,\"kind\":\"universal\",\"name\":\"Лайфлат\"}}";

        [Fact]
        public async Task UsesTheStoredDocumentWithoutAskingTheSite()
        {
            var cache = new MemoryCache(Product);
            var handler = new StubHttpMessageHandler(_ => Json(Product));
            var service = Service(handler, cache);

            var result = await service.GetProductAsync(SiteRegion.Com, "layflat");

            Assert.True(result.IsSuccess);
            Assert.Equal(4494, result.Value!.Id);
            Assert.Equal("Лайфлат", result.Value.Title);
            Assert.Equal(0, handler.Calls);
        }

        [Fact]
        public async Task FetchesOnceAndKeepsTheDocumentForNextTime()
        {
            var cache = new MemoryCache();
            var handler = new StubHttpMessageHandler(_ => Json(Product));
            var service = Service(handler, cache);

            var result = await service.GetProductAsync(SiteRegion.Com, "layflat");

            Assert.True(result.IsSuccess);
            Assert.Equal(1, handler.Calls);
            Assert.Contains("/api/page/data?slug=layflat", Assert.Single(handler.Urls));
            Assert.Equal(Product, cache.Documents["page-data:layflat"]);

            // The second visit must not spend another request on the same product.
            await service.GetProductAsync(SiteRegion.Com, "layflat");
            Assert.Equal(1, handler.Calls);
        }

        [Fact]
        public async Task AsksAgainWhenTheStoredDocumentCannotBeRead()
        {
            // A document from an older parser is a leftover, not an answer.
            var cache = new MemoryCache("{\"not\":\"a product\"}");
            var handler = new StubHttpMessageHandler(_ => Json(Product));
            var service = Service(handler, cache);

            var result = await service.GetProductAsync(SiteRegion.Com, "layflat");

            Assert.True(result.IsSuccess);
            Assert.Equal(1, handler.Calls);
            Assert.Equal(Product, cache.Documents["page-data:layflat"]);
        }

        [Fact]
        public async Task ListsTheProductsInTheSitesOwnOrderWithTheirFormats()
        {
            const string list =
                "[{\"id\":4494,\"name\":\"Лайфлат \",\"kind\":\"kit\",\"order\":40," +
                "\"components\":[{\"id\":9255,\"name\":\"21x30\"},{\"id\":9257,\"name\":\"20x20\"}]}," +
                "{\"id\":5387,\"name\":\"Фотокнига Оптимум\",\"kind\":\"kit\",\"order\":1," +
                "\"components\":[{\"id\":11421,\"name\":\"21x30\"}]}]";

            var cache = new MemoryCache();
            var handler = new StubHttpMessageHandler(_ => Json(list));
            var service = Service(handler, cache);

            var result = await service.GetProductsAsync(SiteRegion.Com);

            Assert.True(result.IsSuccess);
            var products = result.Value!;
            Assert.Equal(new[] { "Фотокнига Оптимум", "Лайфлат" }, products.Select(product => product.Name));
            Assert.Equal(2, products[1].Formats.Count);
            Assert.Equal(9257, products[1].Formats[1].Id);
            Assert.Contains("/creators/photo-book-uploader/products", Assert.Single(handler.Urls));
            Assert.Equal(list, cache.Documents["products-list"]);
        }

        [Fact]
        public async Task UsesTheStoredProductListWithoutAskingTheSite()
        {
            const string list = "[{\"id\":4494,\"name\":\"Лайфлат\",\"kind\":\"kit\",\"order\":1," +
                "\"components\":[{\"id\":9255,\"name\":\"21x30\"}]}]";
            var cache = new MemoryCache();
            cache.Documents["products-list"] = list;
            var handler = new StubHttpMessageHandler(_ => Json(list));
            var service = Service(handler, cache);

            var result = await service.GetProductsAsync(SiteRegion.Com);

            Assert.True(result.IsSuccess);
            Assert.Single(result.Value!);
            Assert.Equal(0, handler.Calls);
        }

        [Fact]
        public async Task AsksForTheChosenFormatByItsTwoIdsAndKeepsTheAnswer()
        {
            const string document = "{\"productTitle\":\"Лайфлат\",\"productPartsTitles\":{\"94\":\"Разворот\"}," +
                "\"integration\":{\"elements\":[{\"settings\":{\"pagesCountPartId\":\"94\"}}]}}";
            var cache = new MemoryCache();
            var handler = new StubHttpMessageHandler(_ => Json(document));
            var service = Service(handler, cache);

            var result = await service.GetFormatAsync(SiteRegion.Com, 4494, 9255);

            Assert.True(result.IsSuccess);
            Assert.Equal("Лайфлат", result.Value!.Title);
            Assert.True(result.Value!.Variants[0].Parts[0].HoldsPages);
            var url = Assert.Single(handler.Urls);
            Assert.Contains("products/data", url, StringComparison.Ordinal);
            Assert.Contains("productId=4494", url, StringComparison.Ordinal);
            Assert.Contains("productComponentId=9255", url, StringComparison.Ordinal);
            Assert.Equal(document, cache.Documents["format:4494:9255"]);
        }

        [Fact]
        public async Task ReadsTheGeometryFromTheSameDocumentWithoutAskingAgain()
        {
            const string document = "{\"productTitle\":\"Лайфлат\",\"integration\":{\"id\":37556,\"settings\":" +
                "{\"outputResolution\":\"300\"},\"elements\":[{\"id\":26129,\"settings\":" +
                "{\"productPartId\":\"94\",\"pagesCountPartId\":\"94\",\"imageSizeWay\":\"manually\"," +
                "\"imageSizeWidth\":\"410\",\"imageSizeHeight\":\"290\"}}]}}";
            var cache = new MemoryCache();
            var handler = new StubHttpMessageHandler(_ => Json(document));
            var service = Service(handler, cache);

            await service.GetFormatAsync(SiteRegion.Com, 4494, 9255);
            var layout = await service.GetLayoutAsync(SiteRegion.Com, 4494, 9255);

            // One answer, two readers: the geometry comes from the document the format already
            // fetched, so a page checking files costs the site nothing extra.
            Assert.True(layout.IsSuccess);
            Assert.Equal(37556, layout.Value!.IntegrationId);
            Assert.Equal(300, layout.Value!.ResolutionDpi);
            Assert.Single(handler.Urls);
        }

        [Fact]
        public async Task ReportsTheSitesRefusalInsteadOfAnEmptyCatalog()
        {
            var cache = new MemoryCache();
            var handler = new StubHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{}")
                });
            var service = Service(handler, cache);

            var result = await service.GetProductAsync(SiteRegion.Com, "layflat");

            Assert.False(result.IsSuccess);
            Assert.Empty(cache.Documents);
        }

        private static SiteCatalogService Service(StubHttpMessageHandler handler, MemoryCache cache) =>
            new(new HttpClient(handler), cache, new SiteProductParser(), new SiteProductsParser(), new SiteFormatParser(), new SiteLayoutParser(), TimeSpan.Zero);

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body) };

        private sealed class MemoryCache : ISiteCatalogCache
        {
            public Dictionary<string, string> Documents { get; } = new(StringComparer.Ordinal);

            public MemoryCache(params string[] contents)
            {
                if (contents.Length > 0)
                    Documents["page-data:layflat"] = contents[0];
            }

            public CachedSiteDocument? Read(SiteRegion region, string document) =>
                Documents.TryGetValue(document, out var content)
                    ? new CachedSiteDocument(content, DateTimeOffset.UtcNow, false)
                    : null;

            public void Write(SiteRegion region, string document, string content) =>
                Documents[document] = content;

            public void Clear() => Documents.Clear();
        }
    }
}
