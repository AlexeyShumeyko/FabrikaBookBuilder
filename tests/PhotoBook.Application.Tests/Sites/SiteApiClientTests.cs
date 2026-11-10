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

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(2, handler.Calls);
        }

        [Fact]
        public async Task Does_not_retry_an_expired_session()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Unauthorized());

            var result = await CreateClient(handler).GetAsync("creators/projects", "token", default);

            Assert.False(result.IsSuccess);
            Assert.Equal(1, handler.Calls);
            Assert.Equal(SiteApiErrorKind.Unauthorized, result.Error!.Kind);
            Assert.Equal(401, result.Error.StatusCode);
            Assert.True(result.Error.IsSessionExpired);
        }

        [Fact]
        public async Task Reports_a_missing_project_as_not_found()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Json("{}", HttpStatusCode.NotFound));

            var result = await CreateClient(handler).GetAsync("creators/projects/edit?projectId=1", "token", default);

            Assert.False(result.IsSuccess);
            Assert.Equal(SiteApiErrorKind.NotFound, result.Error!.Kind);
            Assert.Equal(404, result.Error.StatusCode);
            Assert.False(result.Error.IsRetryable);
            Assert.Equal(1, handler.Calls);
        }

        [Fact]
        public async Task Reports_a_large_file_as_too_large()
        {
            var handler = new StubHttpMessageHandler(_ =>
                StubResponses.Json("{}", HttpStatusCode.RequestEntityTooLarge));

            var result = await CreateClient(handler).GetAsync("creators/projects", "token", default);

            Assert.False(result.IsSuccess);
            Assert.Equal(SiteApiErrorKind.FileTooLarge, result.Error!.Kind);
            Assert.Equal(413, result.Error.StatusCode);
        }

        [Fact]
        public async Task Reports_a_broken_server_as_retryable()
        {
            var handler = new StubHttpMessageHandler(_ => StubResponses.Json("{}", HttpStatusCode.BadGateway));

            var result = await CreateClient(handler).GetAsync("creators/projects", "token", default);

            Assert.Equal(SiteApiErrorKind.ServerUnavailable, result.Error!.Kind);
            Assert.Equal(502, result.Error.StatusCode);
            Assert.True(result.Error.IsRetryable);
        }

        [Fact]
        public async Task Reports_an_answer_that_is_not_json()
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>maintenance</html>")
            });

            var result = await CreateClient(handler).GetAsync("creators/projects", "token", default);

            Assert.False(result.IsSuccess);
            Assert.Equal(SiteApiErrorKind.MalformedResponse, result.Error!.Kind);
        }

        [Fact]
        public async Task Reports_an_empty_answer()
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty)
            });

            var result = await CreateClient(handler).GetAsync("creators/projects", "token", default);

            Assert.False(result.IsSuccess);
            Assert.Equal(SiteApiErrorKind.EmptyResponse, result.Error!.Kind);
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
