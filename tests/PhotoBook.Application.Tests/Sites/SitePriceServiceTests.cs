using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Pricing;
using PhotoBook.Infrastructure.Sites;

namespace PhotoBook.Application.Tests.Sites
{
    /// <summary>
    /// The price endpoint answers 200 for both "this set costs 542" and "this set is not
    /// sellable", so the service is tested for telling those apart from an outright failure.
    /// </summary>
    /// <remarks>
    /// The request shape is pinned too: the platform answered zero for every combination until
    /// the call matched what the calculator page sends, and that regression is silent.
    /// </remarks>
    public class SitePriceServiceTests
    {
        private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

        private static SiteSession Session() => new(SiteRegion.Com, "token-value", Now.AddDays(7));

        private static PriceQuery Query() => new(
            4494,
            9255,
            new Dictionary<long, long>
            {
                [70519] = 450642,
                [65075] = 417892
            },
            new Dictionary<long, int>
            {
                [94] = 10,
                [93] = 1
            });

        private static SitePriceService Create(StubHttpMessageHandler handler) =>
            new(new HttpClient(handler), TimeSpan.FromMilliseconds(1));

        [Fact]
        public async Task ReadsThePriceAndItsCurrency()
        {
            var check = await Create(new StubHttpMessageHandler(_ => StubResponses.Json(
                    "{\"price\":542,\"formattedPrice\":\"542,00 RUB\",\"priceWithDiscount\":542}")))
                .CheckAsync(Session(), Query());

            Assert.True(check.IsValid, check.Error);
            Assert.Equal(SitePriceOutcome.Valid, check.Outcome);
            Assert.Equal(542m, check.Quote!.Amount);
            Assert.Equal("542,00 RUB", check.Quote.Formatted);
            Assert.Equal("RUB", check.Quote.Currency);
        }

        [Fact]
        public async Task ReportsZeroAsAnUnavailableCombinationRatherThanAFreeOne()
        {
            var check = await Create(new StubHttpMessageHandler(_ => StubResponses.Json(
                    "{\"price\":0,\"formattedPrice\":\"0,00 RUB\"}")))
                .CheckAsync(Session(), Query());

            Assert.False(check.IsValid);
            Assert.Equal(SitePriceOutcome.Invalid, check.Outcome);
            Assert.Null(check.Quote);
            Assert.Equal("Комбинация недоступна.", check.Error);
        }

        [Fact]
        public async Task RefusesAnAnswerWithoutAPrice()
        {
            var check = await Create(new StubHttpMessageHandler(_ => StubResponses.Json("{\"data\":{}}")))
                .CheckAsync(Session(), Query());

            Assert.Equal(SitePriceOutcome.Error, check.Outcome);
            Assert.Equal("Сайт не назвал цену книги.", check.Error);
        }

        [Fact]
        public async Task RefusesAPriceThatIsNotANumber()
        {
            var check = await Create(new StubHttpMessageHandler(_ => StubResponses.Json(
                    "{\"price\":\"542,00\",\"formattedPrice\":\"542,00 RUB\"}")))
                .CheckAsync(Session(), Query());

            Assert.Equal(SitePriceOutcome.Error, check.Outcome);
        }

        [Fact]
        public async Task AsksTheSiteHostWithTheShapesTheCalculatorUses()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Json(
                "{\"price\":1410,\"formattedPrice\":\"1410,00 RUB\"}"));

            await Create(handler).CheckAsync(Session(), Query());

            var url = Assert.Single(handler.Urls);
            Assert.StartsWith("https://fabrika-fotoknigi.com/api/products/price?", url);
            Assert.Contains("productId=4494", url);
            Assert.Contains("params%5BproductComponentId%5D=9255", url);
            Assert.Contains("params%5Boptions%5D%5B65075%5D=417892", url);
            Assert.Contains("params%5Boptions%5D%5B70519%5D=450642", url);
            Assert.Contains("params%5BproductPartQuantity%5D%5B93%5D=1", url);
            Assert.Contains("params%5BproductPartQuantity%5D%5B94%5D=10", url);
        }

        [Fact]
        public async Task WritesOptionsInIdOrderSoTheSameCombinationIsTheSameUrl()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Json("{\"price\":1}"));

            await Create(handler).CheckAsync(Session(), Query());

            var url = Assert.Single(handler.Urls);
            Assert.True(
                url.IndexOf("65075", StringComparison.Ordinal) <
                url.IndexOf("70519", StringComparison.Ordinal),
                url);
        }

        [Fact]
        public async Task SendsTheSessionToken()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Json("{\"price\":1}"));

            await Create(handler).CheckAsync(Session(), Query());

            Assert.Equal("Bearer token-value", Assert.Single(handler.Authorizations));
        }

        [Fact]
        public async Task ReportsAServerFailureAfterTheAllowedRetries()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Json(
                "{\"message\":\"Server Error\"}", HttpStatusCode.BadGateway));

            var check = await Create(handler).CheckAsync(Session(), Query());

            Assert.Equal(SitePriceOutcome.Error, check.Outcome);
            Assert.Equal(3, handler.Calls);
            Assert.Contains("временно недоступен", check.Error);
        }

        [Fact]
        public async Task DoesNotRetryAnExpiredSession()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Unauthorized());

            var check = await Create(handler).CheckAsync(Session(), Query());

            Assert.Equal(SitePriceOutcome.Error, check.Outcome);
            Assert.Equal(1, handler.Calls);
            Assert.Contains("войти заново", check.Error);
        }

        [Fact]
        public async Task KeepsAPriceWithoutItsFormattedText()
        {
            var check = await Create(new StubHttpMessageHandler(_ => StubResponses.Json("{\"price\":99}")))
                .CheckAsync(Session(), Query());

            Assert.True(check.IsValid, check.Error);
            Assert.Equal(string.Empty, check.Quote!.Formatted);
            Assert.Equal(string.Empty, check.Quote.Currency);
        }
    }
}
