using System.Net.Http;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Infrastructure.Sites;

namespace PhotoBook.Application.Tests.Sites
{
    /// <summary>
    /// Signing in without asking the customer which site the account belongs to.
    /// </summary>
    public class SiteAuthServiceTests
    {
        private static readonly System.DateTimeOffset Now =
            new(2026, 11, 10, 12, 0, 0, System.TimeSpan.Zero);

        [Fact]
        public async Task Signs_in_on_the_site_that_accepts_the_password()
        {
            var handler = new StubHttpMessageHandler(request => IsByRequest(request)
                ? StubResponses.Unauthorized()
                : StubResponses.Json("{\"access_token\":\"token-com\",\"token_type\":\"bearer\",\"expires_in\":604800}"));

            var result = await CreateService(handler).SignInAsync("customer@example.com", "secret");

            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(SiteRegion.Com.Code, result.Value!.Region.Code);
            Assert.Equal("token-com", result.Value.AccessToken);
            Assert.Equal(Now.AddDays(7), result.Value.ExpiresAt);
        }

        [Fact]
        public async Task Prefers_the_site_where_the_account_holds_projects()
        {
            var handler = new StubHttpMessageHandler(request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/api/auth/login", System.StringComparison.OrdinalIgnoreCase))
                    return StubResponses.Json("{\"access_token\":\"token\",\"expires_in\":604800}");

                return IsByRequest(request)
                    ? StubResponses.Json("{\"data\":[{\"id\":1}]}")
                    : StubResponses.Json("{\"data\":[]}");
            });

            var result = await CreateService(handler).SignInAsync("customer@example.com", "secret");

            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(SiteRegion.By.Code, result.Value!.Region.Code);
        }

        [Fact]
        public async Task Reports_a_wrong_password_without_throwing()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Unauthorized());

            var result = await CreateService(handler).SignInAsync("customer@example.com", "wrong");

            Assert.False(result.IsSuccess);
            Assert.Equal("Неверная почта или пароль.", result.Error);
        }

        [Fact]
        public async Task Reports_a_platform_refusal_as_a_wrong_password()
        {
            // The live platform answers 422 to a refused password, not 401: its validation
            // error, which the customer must read as one sentence and not as a status code.
            var handler = new StubHttpMessageHandler(_ => StubResponses.Json(
                "{\"message\":\"The given data was invalid.\"}",
                System.Net.HttpStatusCode.UnprocessableEntity));

            var result = await CreateService(handler).SignInAsync("customer@example.com", "wrong");

            Assert.False(result.IsSuccess);
            Assert.Equal("Неверная почта или пароль.", result.Error);
        }

        [Fact]
        public async Task Does_not_call_the_sites_without_credentials()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Json("{}"));

            var result = await CreateService(handler).SignInAsync("  ", string.Empty);

            Assert.False(result.IsSuccess);
            Assert.Equal(0, handler.Calls);
        }

        [Fact]
        public async Task Asks_both_sites_even_when_the_first_one_answers()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Unauthorized());

            await CreateService(handler).SignInAsync("customer@example.com", "secret");

            Assert.Equal(2, handler.Calls);
        }

        private static SiteAuthService CreateService(StubHttpMessageHandler handler)
            => new(new HttpClient(handler), () => Now, System.TimeSpan.FromMilliseconds(1));

        /// <summary>
        /// Which site a request belongs to. The login call goes to the site host, while the
        /// activity call goes to the shared API host and is told apart by the site prefix, which
        /// is exactly why the region cannot be read off the domain alone.
        /// </summary>
        private static bool IsByRequest(HttpRequestMessage request)
        {
            var uri = request.RequestUri!;

            return uri.Host.EndsWith(".by", System.StringComparison.OrdinalIgnoreCase) ||
                   uri.AbsolutePath.StartsWith("/site/lzopu", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
