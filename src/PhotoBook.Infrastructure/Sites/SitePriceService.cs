using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Pricing;

namespace PhotoBook.Infrastructure.Sites
{
    /// <summary>
    /// Reads the price the site's calculator reads, from the same endpoint the page calls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The request is built to match the browser exactly: the same query names, the same
    /// element ids and quantities, on the site host. Anything else was measured to answer zero
    /// for combinations the site sells, which is worse than an error - it looks like a refusal.
    /// </para>
    /// <para>
    /// Zero is not passed on as a number. It is the site's way of saying "this set is not
    /// sellable", and the outcome is reported as such, so nothing in the window can present an
    /// unsellable book as free.
    /// </para>
    /// </remarks>
    internal sealed class SitePriceService : ISitePriceService
    {
        private const string PricePath = "/api/products/price";

        private readonly HttpClient _http;
        private readonly TimeSpan _retryDelay;

        public SitePriceService(HttpClient http, TimeSpan? retryDelay = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(500);
        }

        public async Task<SitePriceCheck> CheckAsync(
            SiteSession session,
            PriceQuery query,
            CancellationToken cancellationToken = default)
        {
            if (session is null)
                throw new ArgumentNullException(nameof(session));
            if (query is null)
                throw new ArgumentNullException(nameof(query));

            var client = new SiteApiClient(session.Region, _http, _retryDelay);
            var response = await client
                .GetOnSiteAsync(PricePath + "?" + BuildQuery(query), session.AccessToken, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccess || response.Document is null)
                return SitePriceCheck.Failed(response.Error?.Message ?? "Сайт не назвал цену.");

            using var document = response.Document;

            return Read(document.RootElement);
        }

        /// <summary>The query in the shape the calculator page sends it.</summary>
        /// <remarks>
        /// Options are written in id order so the same combination always produces the same
        /// URL; that keeps logs and caches comparable instead of differing by chance.
        /// </remarks>
        internal static string BuildQuery(PriceQuery query)
        {
            var parts = new List<string>
            {
                "productId=" + query.ProductId.ToString(CultureInfo.InvariantCulture)
            };

            if (query.ProductComponentId is { } component)
            {
                parts.Add(
                    Escape("params[productComponentId]") + "=" +
                    component.ToString(CultureInfo.InvariantCulture));
            }

            foreach (var option in (query.Options ?? new Dictionary<long, long>()).OrderBy(pair => pair.Key))
            {
                parts.Add(
                    Escape($"params[options][{option.Key}]") + "=" +
                    option.Value.ToString(CultureInfo.InvariantCulture));
            }

            foreach (var part in (query.PartQuantities ?? new Dictionary<long, int>()).OrderBy(pair => pair.Key))
            {
                parts.Add(
                    Escape($"params[productPartQuantity][{part.Key}]") + "=" +
                    part.Value.ToString(CultureInfo.InvariantCulture));
            }

            return string.Join("&", parts);
        }

        private static SitePriceCheck Read(JsonElement root)
        {
            if (!root.TryGetProperty("price", out var priceElement) ||
                priceElement.ValueKind != JsonValueKind.Number ||
                !priceElement.TryGetDecimal(out var amount))
            {
                return SitePriceCheck.Failed("Сайт не назвал цену книги.");
            }

            if (amount <= 0)
                return SitePriceCheck.Invalid();

            var formatted = root.TryGetProperty("formattedPrice", out var formattedElement) &&
                            formattedElement.ValueKind == JsonValueKind.String
                ? formattedElement.GetString() ?? string.Empty
                : string.Empty;

            return SitePriceCheck.Valid(new SitePriceQuote(amount, formatted, CurrencyOf(formatted)));
        }

        /// <summary>The currency is the last word of the site's own text: «542,00 RUB».</summary>
        private static string CurrencyOf(string formatted)
        {
            if (string.IsNullOrWhiteSpace(formatted))
                return string.Empty;

            var words = formatted.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return words.Length == 0 ? string.Empty : words[^1];
        }

        private static string Escape(string value) => Uri.EscapeDataString(value);
    }
}
