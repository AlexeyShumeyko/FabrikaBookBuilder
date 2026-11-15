using System;
using System.Collections.Generic;
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
            new(new HttpClient(handler), cache, new SiteProductParser(), TimeSpan.Zero);

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
