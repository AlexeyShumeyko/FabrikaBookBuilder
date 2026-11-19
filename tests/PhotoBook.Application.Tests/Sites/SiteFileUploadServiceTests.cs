using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Upload;
using PhotoBook.Infrastructure.Sites;

namespace PhotoBook.Application.Tests.Sites
{
    /// <summary>
    /// The upload endpoint answers 200 for files it cannot read and refuses in ways the screen has
    /// to tell apart: a session that ended, a rate limit worth waiting out, a photograph worth
    /// replacing. These tests pin the translation.
    /// </summary>
    public class SiteFileUploadServiceTests
    {
        private static readonly SiteSession Session =
            new(SiteRegion.Com, "token-value", new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

        [Fact]
        public async Task SendsTheProjectThePositionAndTheFile()
        {
            using var page = new TempPage();
            var handler = new CapturingHandler();

            var sent = await Service(handler).UploadAsync(Session, 527733, 0, 2, page.Path);

            Assert.True(sent.IsAccepted);
            Assert.Contains("name=projectId", handler.Body);
            Assert.Contains("527733", handler.Body);
            Assert.Contains("name=position", handler.Body);
            Assert.Contains("name=file", handler.Body);
            Assert.Contains(System.IO.Path.GetFileName(page.Path), handler.Body);
            Assert.Contains("creators/photo-book-uploader/files/upload", handler.Url);
        }

        /// <summary>
        /// The site's own uploader names the picture it sends, and the platform keeps those
        /// fields on the page: a page sent without them arrived with no name and no size.
        /// </summary>
        [Fact]
        public async Task NamesThePictureAndItsSizeAsTheSitesOwnUploaderDoes()
        {
            using var page = new TempPage();
            var handler = new CapturingHandler();

            var sent = await Service(handler).UploadAsync(Session, 527733, 0, 2, page.Path, 4913, 3496);

            Assert.True(sent.IsAccepted);
            // A bracketed field name is written as a quoted-string by the transport, which is
            // the same name to the site's parser; the quotes are the wire format, not the key.
            Assert.Contains("name=\"params[fileName]\"", handler.Body);
            Assert.Contains(System.IO.Path.GetFileName(page.Path), handler.Body);
            Assert.Contains("name=\"params[fileWidth]\"", handler.Body);
            Assert.Contains("4913", handler.Body);
            Assert.Contains("name=\"params[fileHeight]\"", handler.Body);
            Assert.Contains("3496", handler.Body);
            Assert.Contains("name=\"params[checksum]\"", handler.Body);
        }

        [Fact]
        public async Task LeavesOutASizeThatWasNeverRead()
        {
            using var page = new TempPage();
            var handler = new CapturingHandler();

            await Service(handler).UploadAsync(Session, 527733, 0, 2, page.Path);

            // Zero is "not known", not "nothing": the site must not be told a picture is 0×0.
            Assert.DoesNotContain("params[fileWidth]", handler.Body);
            Assert.DoesNotContain("params[fileHeight]", handler.Body);
        }

        [Fact]
        public async Task NamesTheBookTheFileBelongsTo()
        {
            using var page = new TempPage();
            var handler = new CapturingHandler();

            var sent = await Service(handler).UploadAsync(Session, 527733, 65075, 2, page.Path);

            Assert.True(sent.IsAccepted);
            Assert.Contains("name=projectItemId", handler.Body);
            Assert.Contains("65075", handler.Body);
        }

        /// <summary>
        /// The picture the project already holds is placed by its number: no file in the request,
        /// which is what makes a repeated spread cost one upload instead of one per book.
        /// </summary>
        [Fact]
        public async Task CopiesAStoredPictureWithoutSendingTheFile()
        {
            var handler = new CapturingHandler();

            var sent = await Service(handler).CopyAsync(Session, 527733, 65075, 3, 18954166, "shared.jpg", 4913, 3496);

            Assert.True(sent.IsAccepted);
            Assert.Contains("name=doublePageId", handler.Body);
            Assert.Contains("18954166", handler.Body);
            Assert.Contains("name=position", handler.Body);
            Assert.Contains("name=projectItemId", handler.Body);
            Assert.Contains("name=\"params[fileName]\"", handler.Body);
            Assert.DoesNotContain("name=file", handler.Body);
        }

        /// <summary>
        /// The answer names the picture, and the number is what the next page copies from; the
        /// site's own uploader reads it from the same field.
        /// </summary>
        [Fact]
        public async Task ReadsTheNumberOfThePictureTheSiteStored()
        {
            using var page = new TempPage();
            var handler = new CapturingHandler(
                "{\"double_pages\":[{\"id\":18954166,\"project_item_id\":2832059,\"position\":\"2\"}]}");

            var sent = await Service(handler).UploadAsync(Session, 527733, 65075, 2, page.Path);

            Assert.True(sent.IsAccepted);
            Assert.Equal(18954166, sent.ImageId);
        }

        [Fact]
        public async Task AFileTheSiteCannotReadIsNotSuccessEvenWithA200()
        {
            using var page = new TempPage();
            var handler = new StubHttpMessageHandler(_ => Json(
                HttpStatusCode.OK,
                "{\"corrupted_file\":\"The file is corrupted.\"}"));

            var sent = await Service(handler).UploadAsync(Session, 527733, 0, 0, page.Path);

            Assert.False(sent.IsAccepted);
            Assert.Equal(UploadFailureKind.File, sent.Kind);
            Assert.Contains("Проверьте фотографию", sent.Error);
        }

        [Fact]
        public async Task AnExpiredSessionAsksForThePasswordRatherThanARetry()
        {
            using var page = new TempPage();
            var handler = new StubHttpMessageHandler(_ => Json(HttpStatusCode.Unauthorized, "{}"));

            var sent = await Service(handler).UploadAsync(Session, 527733, 0, 0, page.Path);

            Assert.Equal(UploadFailureKind.Unauthorized, sent.Kind);
            Assert.True(sent.NeedsSignIn);
            Assert.False(sent.CanResume);
            Assert.Equal(1, handler.Calls);
        }

        [Fact]
        public async Task RefusedParametersAreNotWorthResendingTheSameRequest()
        {
            using var page = new TempPage();
            var handler = new StubHttpMessageHandler(_ => Json(HttpStatusCode.UnprocessableEntity, "{}"));

            var sent = await Service(handler).UploadAsync(Session, 527733, 0, 0, page.Path);

            Assert.Equal(UploadFailureKind.Parameters, sent.Kind);
            Assert.False(sent.CanResume);
        }

        [Fact]
        public async Task ARateLimitIsNotRetriedByTheUploader()
        {
            using var page = new TempPage();
            var handler = new StubHttpMessageHandler(_ => Json(HttpStatusCode.TooManyRequests, "{}"));

            var sent = await Service(handler).UploadAsync(Session, 527733, 0, 0, page.Path);

            Assert.Equal(UploadFailureKind.RateLimited, sent.Kind);
            Assert.True(sent.CanResume);

            // A page sent twice at the same position may land twice, so the customer decides.
            Assert.Equal(1, handler.Calls);
        }

        [Fact]
        public async Task AServerErrorIsReportedAsWorthWaitingOut()
        {
            using var page = new TempPage();
            var handler = new StubHttpMessageHandler(_ => Json(HttpStatusCode.ServiceUnavailable, "{}"));

            var sent = await Service(handler).UploadAsync(Session, 527733, 0, 0, page.Path);

            Assert.Equal(UploadFailureKind.ServerUnavailable, sent.Kind);
            Assert.True(sent.CanResume);
        }

        [Fact]
        public async Task ADroppedConnectionIsReportedAsANetworkProblem()
        {
            using var page = new TempPage();
            var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("no route"));

            var sent = await Service(handler).UploadAsync(Session, 527733, 0, 0, page.Path);

            Assert.Equal(UploadFailureKind.ConnectionLost, sent.Kind);
            Assert.True(sent.CanResume);
            Assert.Contains("Нет связи", sent.Error);
        }

        private static SiteFileUploadService Service(HttpMessageHandler handler) =>
            new(new HttpClient(handler));

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        /// <summary>Reads the multipart body where waiting is allowed: inside an async send.</summary>
        private sealed class CapturingHandler : HttpMessageHandler
        {
            private readonly string _answer;

            internal CapturingHandler(string answer = "{\"double_pages\":[]}")
            {
                _answer = answer;
            }

            internal string Body { get; private set; } = string.Empty;

            internal string Url { get; private set; } = string.Empty;

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Url = request.RequestUri?.ToString() ?? string.Empty;

                return Json(HttpStatusCode.OK, _answer);
            }
        }

        private sealed class TempPage : IDisposable
        {
            internal TempPage()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "page-" + Guid.NewGuid().ToString("N") + ".jpg");

                System.IO.File.WriteAllBytes(Path, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
            }

            internal string Path { get; }

            public void Dispose()
            {
                try
                {
                    System.IO.File.Delete(Path);
                }
                catch (System.IO.IOException)
                {
                    // A leftover test file is not a failing test.
                }
            }
        }
    }
}
