using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoBook.Application.Tests.Sites
{
    /// <summary>
    /// A transport that answers from a function instead of the network, and records what it was
    /// asked, so a test can assert on the request as well as on the answer.
    /// </summary>
    internal sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public int Calls { get; private set; }

        public List<string> Urls { get; } = new();

        public List<string> UserAgents { get; } = new();

        public List<string> Authorizations { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Urls.Add(request.RequestUri?.ToString() ?? string.Empty);
            UserAgents.Add(request.Headers.UserAgent.ToString());
            Authorizations.Add(request.Headers.Authorization?.ToString() ?? string.Empty);

            return Task.FromResult(_responder(request));
        }
    }
}
