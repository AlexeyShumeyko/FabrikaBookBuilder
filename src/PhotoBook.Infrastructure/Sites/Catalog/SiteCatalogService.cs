using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Application.Sites.Catalog.Layout;

namespace PhotoBook.Infrastructure.Sites.Catalog
{
    /// <summary>
    /// Reads a product from the cache, or from the catalog page when nothing is stored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One request per product per installed copy, and only when the stored document is missing:
    /// the catalog does not change by the minute, and the platform is shared by two sites. A
    /// stale document is returned as it is - the cache already says how old it is, and hiding it
    /// would leave the window empty on a train.
    /// </para>
    /// <para>
    /// The parse result is what is stored, through the cache, never a half-understood shape: the
    /// parser is the single place that knows the document, and a parser change bumps the stored
    /// schema rather than leaving old files to be read as new ones.
    /// </para>
    /// </remarks>
    internal sealed class SiteCatalogService : ISiteCatalogService
    {
        private const string ProductDocument = "page-data";
        private const string ProductsDocument = "products-list";
        private const string ProductsPath = "creators/photo-book-uploader/products";
        private const string FormatPath = "creators/photo-book-uploader/products/data";
        private const string FormatDocument = "format";

        private readonly HttpClient _http;
        private readonly ISiteCatalogCache _cache;
        private readonly ISiteProductParser _parser;
        private readonly ISiteProductsParser _products;
        private readonly ISiteFormatParser _format;
        private readonly ISiteLayoutParser _layout;
        private readonly TimeSpan _retryDelay;

        public SiteCatalogService(
            HttpClient http,
            ISiteCatalogCache cache,
            ISiteProductParser parser,
            ISiteProductsParser products,
            ISiteFormatParser format,
            ISiteLayoutParser layout,
            TimeSpan? retryDelay = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
            _products = products ?? throw new ArgumentNullException(nameof(products));
            _format = format ?? throw new ArgumentNullException(nameof(format));
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(500);
        }

        public async Task<Result<IReadOnlyList<CatalogProduct>>> GetProductsAsync(
            SiteRegion region,
            CancellationToken cancellationToken = default)
        {
            if (region is null)
                throw new ArgumentNullException(nameof(region));

            var stored = _cache.Read(region, ProductsDocument);
            if (stored is not null)
            {
                var cached = _products.Parse(stored.Content);
                if (cached.IsSuccess)
                    return cached;
            }

            var client = new SiteApiClient(region, _http, _retryDelay);
            var answer = await client
                .GetAsync(ProductsPath, token: null, cancellationToken)
                .ConfigureAwait(false);

            if (!answer.IsSuccess || answer.Document is null)
            {
                var reason = answer.Error?.Message;
                return Result<IReadOnlyList<CatalogProduct>>.Fail(string.IsNullOrWhiteSpace(reason)
                    ? "Список продуктов сайта не открылся."
                    : reason!);
            }

            var json = answer.Document.RootElement.GetRawText();
            answer.Document.Dispose();

            var parsed = _products.Parse(json);
            if (parsed.IsSuccess)
                _cache.Write(region, ProductsDocument, json);

            return parsed;
        }

        public async Task<Result<SiteProduct>> GetFormatAsync(
            SiteRegion region,
            long productId,
            long componentId,
            CancellationToken cancellationToken = default)
        {
            if (region is null)
                throw new ArgumentNullException(nameof(region));
            if (productId <= 0 || componentId <= 0)
                throw new ArgumentException("Формат опознаётся парой идентификаторов, и оба нужны.");

            var document = FormatDocument + ":" + productId + ":" + componentId;

            var stored = _cache.Read(region, document);
            if (stored is not null)
            {
                var cached = _format.Parse(stored.Content, productId, componentId);
                if (cached.IsSuccess)
                    return cached;
            }

            var fetched = await FetchFormatAsync(region, productId, componentId, document, cancellationToken)
                .ConfigureAwait(false);

            if (!fetched.IsSuccess || fetched.Value is null)
                return Result<SiteProduct>.Fail(fetched.Error ?? "Описание формата не открылось.");

            return _format.Parse(fetched.Value, productId, componentId);
        }

        /// <summary>
        /// The geometry of a format, from the document the catalog already keeps.
        /// </summary>
        /// <remarks>
        /// The same document answers both questions, so a page that asks for the format and then
        /// for its geometry costs one request: the second call reads what the first stored.
        /// </remarks>
        public async Task<Result<SiteLayout>> GetLayoutAsync(
            SiteRegion region,
            long productId,
            long componentId,
            CancellationToken cancellationToken = default)
        {
            if (region is null)
                throw new ArgumentNullException(nameof(region));
            if (productId <= 0 || componentId <= 0)
                throw new ArgumentException("Формат опознаётся парой идентификаторов, и оба нужны.");

            var document = FormatDocument + ":" + productId + ":" + componentId;

            var stored = _cache.Read(region, document);
            if (stored is not null)
            {
                var cached = _layout.Parse(stored.Content);
                if (cached.IsSuccess)
                    return cached;
            }

            var fetched = await FetchFormatAsync(region, productId, componentId, document, cancellationToken)
                .ConfigureAwait(false);

            if (!fetched.IsSuccess || fetched.Value is null)
                return Result<SiteLayout>.Fail(fetched.Error ?? "Геометрия макета не открылась.");

            return _layout.Parse(fetched.Value);
        }

        /// <summary>Asks the site for a format's document and keeps it when it is readable.</summary>
        private async Task<Result<string>> FetchFormatAsync(
            SiteRegion region,
            long productId,
            long componentId,
            string document,
            CancellationToken cancellationToken)
        {
            var client = new SiteApiClient(region, _http, _retryDelay);
            var answer = await client
                .GetAsync(
                    FormatPath +
                    "?productId=" + productId.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "&productComponentId=" + componentId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    token: null,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!answer.IsSuccess || answer.Document is null)
            {
                var reason = answer.Error?.Message;
                return Result<string>.Fail(string.IsNullOrWhiteSpace(reason)
                    ? "Описание формата не открылось."
                    : reason!);
            }

            var json = answer.Document.RootElement.GetRawText();
            answer.Document.Dispose();

            _cache.Write(region, document, json);

            return Result<string>.Ok(json);
        }

        public async Task<Result<SiteProduct>> GetProductAsync(
            SiteRegion region,
            string slug,
            CancellationToken cancellationToken = default)
        {
            if (region is null)
                throw new ArgumentNullException(nameof(region));
            if (string.IsNullOrWhiteSpace(slug))
                throw new ArgumentException("Нужен слаг продукта: по нему продукт и опознаётся.", nameof(slug));

            var document = ProductDocument + ":" + slug;

            var stored = _cache.Read(region, document);
            if (stored is not null)
            {
                var cached = _parser.Parse(stored.Content, slug);
                if (cached.IsSuccess)
                    return cached;

                // A stored document the parser can no longer read is not an answer: it is a
                // leftover from an older parser, and the site is asked instead of failing.
            }

            var client = new SiteApiClient(region, _http, _retryDelay);
            var answer = await client
                .GetOnSiteAsync("/api/page/data?slug=" + Uri.EscapeDataString(slug), token: null, cancellationToken)
                .ConfigureAwait(false);

            if (!answer.IsSuccess || answer.Document is null)
            {
                var reason = answer.Error?.Message;
                return Result<SiteProduct>.Fail(string.IsNullOrWhiteSpace(reason)
                    ? "Каталог сайта не открылся."
                    : reason!);
            }

            var json = answer.Document.RootElement.GetRawText();
            answer.Document.Dispose();

            var parsed = _parser.Parse(json, slug);
            if (parsed.IsSuccess)
                _cache.Write(region, document, json);

            return parsed;
        }
    }
}
