using System.Net;
using System.Net.Http;
using System.Text;

namespace PhotoBook.Application.Tests.Sites
{
    internal static class StubResponses
    {
        public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
            => new(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

        public static HttpResponseMessage Unauthorized()
            => Json("{\"message\":\"\"}", HttpStatusCode.Unauthorized);
    }
}
