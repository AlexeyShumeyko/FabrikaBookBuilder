using System;
using System.Net.Http;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Infrastructure.Sites;

namespace PhotoBook.Application.Tests.Sites
{
    /// <summary>
    /// The profile decides what the header says, so its uneven fields are pinned: a full name, a
    /// first name with a surname, and neither of them.
    /// </summary>
    public class SiteProfileServiceTests
    {
        private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

        private static SiteSession Session() =>
            new(SiteRegion.Com, "token-value", Now.AddDays(7), "customer@example.com");

        private static SiteProfileService Create(StubHttpMessageHandler handler) =>
            new(new HttpClient(handler), TimeSpan.FromMilliseconds(1));

        [Fact]
        public async Task ReadsTheNameTheAddressAndTheId()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Json(
                "{\"id\":11482255,\"name\":\" Анна \",\"email\":\"anna@example.com\"}"));

            var result = await Create(handler).GetAsync(Session());

            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(11482255, result.Value!.Id);
            Assert.Equal("Анна", result.Value.DisplayName);
            Assert.Equal("anna@example.com", result.Value.Email);
            Assert.Contains("site/p4m3j/auth/user", handler.Urls[0]);
            Assert.Equal("Bearer token-value", handler.Authorizations[0]);
        }

        [Fact]
        public async Task JoinsTheFirstnameAndTheSurnameWhenThereIsNoFullName()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Json(
                "{\"id\":7,\"firstname\":\"Анна\",\"surname\":\"Ковальчук\",\"email\":\"anna@example.com\"}"));

            var profile = (await Create(handler).GetAsync(Session())).Value!;

            Assert.Equal("Анна Ковальчук", profile.DisplayName);
        }

        [Fact]
        public async Task FallsBackToTheAddressWhenTheSiteNamesNobody()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Json(
                "{\"id\":7,\"firstname\":\"\",\"surname\":null}"));

            var profile = (await Create(handler).GetAsync(Session())).Value!;

            Assert.Equal("customer@example.com", profile.DisplayName);
            Assert.Equal("customer@example.com", profile.Email);
        }

        [Fact]
        public async Task ReadsAnIdSentAsAString()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Json(
                "{\"id\":\"11482255\",\"name\":\"Анна\"}"));

            var profile = (await Create(handler).GetAsync(Session())).Value!;

            Assert.Equal(11482255, profile.Id);
        }

        [Fact]
        public async Task ReportsARefusedProfileWithoutPretendingToKnowTheName()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Unauthorized());

            var result = await Create(handler).GetAsync(Session());

            Assert.False(result.IsSuccess);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
        }

        [Fact]
        public async Task AsksTheSiteTheSessionWasMadeOn()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Json("{\"id\":1,\"name\":\"Анна\"}"));
            var session = new SiteSession(SiteRegion.By, "token-value", Now.AddDays(7));

            await Create(handler).GetAsync(session);

            Assert.Contains("site/lzopu/auth/user", handler.Urls[0]);
            Assert.Contains("fabrika-fotoknigi.com", handler.Urls[0]);
        }
    }
}
