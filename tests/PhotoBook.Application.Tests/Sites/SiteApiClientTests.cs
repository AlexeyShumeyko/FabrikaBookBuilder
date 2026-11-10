using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Infrastructure.Sites;

namespace PhotoBook.Application.Tests.Sites
{
    /// <summary>
    /// The transport rules: what is retried, what is not, and what every request carries.
    /// </summary>
    public class SiteApiClientTests
    {
        [Fact]
        public async Task Retries_a_rate_limit_and_then_succeeds()
        {
            var calls = 0;
            var handler = new StubHttpMessageHandler(_ => ++calls == 1
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : StubResponses.Json("{\"ok\":true}"));

            var result = await CreateClient(handler).GetAsync("creators/projects", "token", default);

            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(2, handler.Calls);
        }

        [Fact]
        public async Task Does_not_retry_an_expired_session()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Unauthorized());

            var result = await CreateClient(handler).GetAsync("creators/projects", "token", default);

            Assert.False(result.IsSuccess);
            Assert.Equal(1, handler.Calls);
            Assert.Equal("Неверная почта или пароль.", result.Error);
        }

        [Fact]
        public async Task Gives_up_after_three_attempts_on_a_broken_server()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Json("{}", HttpStatusCode.BadGateway));

            var result = await CreateClient(handler).GetAsync("creators/projects", "token", default);

            Assert.False(result.IsSuccess);
            Assert.Equal(3, handler.Calls);
        }

        [Fact]
        public async Task Calls_the_region_prefix_and_sends_the_bearer_token()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Json("{}"));

            await CreateClient(handler).GetAsync("creators/projects", "token-value", default);

            Assert.StartsWith(SiteRegion.Com.ApiRoot + "/creators/projects", handler.Urls[0]);
            Assert.Equal("Bearer token-value", handler.Authorizations[0]);
        }

        [Fact]
        public async Task Identifies_itself_in_the_user_agent()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Json("{}"));

            await CreateClient(handler).GetAsync("creators/projects", "token", default);

            Assert.Contains("BookBuilderStudio", handler.UserAgents[0]);
        }

        private static SiteApiClient CreateClient(StubHttpMessageHandler handler)
            => new(SiteRegion.Com, new HttpClient(handler), TimeSpan.FromMilliseconds(1));
    }
}
