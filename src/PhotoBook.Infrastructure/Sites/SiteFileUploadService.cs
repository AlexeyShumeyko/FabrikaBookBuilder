using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Upload;

namespace PhotoBook.Infrastructure.Sites
{
    /// <summary>Sends one page to the site's uploader endpoint.</summary>
    /// <remarks>
    /// <para>
    /// The endpoint answers <c>200</c> with <c>{"corrupted_file": "The file is corrupted."}</c>
    /// for a file it cannot use, so the body is read and a status code is not trusted. What the
    /// site says is passed on rather than replaced with "the upload failed": the two mean
    /// different things to the customer, one being their file and the other the connection.
    /// </para>
    /// <para>
    /// The call is made once. The retry policy of the other endpoints exists for reads, which
    /// can be repeated safely; a page sent twice at the same position may end up as two pages,
    /// so the customer pressing the button again is a better answer than a silent repeat.
    /// </para>
    /// </remarks>
    internal sealed class SiteFileUploadService : ISiteFileUploadService
    {
        private const string UploadPath = "creators/photo-book-uploader/files/upload";

        private readonly HttpClient _http;

        public SiteFileUploadService(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        public async Task<PageUpload> UploadAsync(
            SiteSession session,
            long projectId,
            int position,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (session is null)
                throw new ArgumentNullException(nameof(session));
            if (projectId <= 0)
                throw new ArgumentOutOfRangeException(nameof(projectId), "У проекта нет номера.");
            if (position < 0)
                throw new ArgumentOutOfRangeException(nameof(position), "Страница не может быть раньше первой.");
            if (string.IsNullOrWhiteSpace(filePath))
                return PageUpload.Refused(UploadFailureKind.File, "Не указан файл страницы.");

            var client = new SiteApiClient(session.Region, _http);

            var fields = new Dictionary<string, string>
            {
                ["projectId"] = projectId.ToString(CultureInfo.InvariantCulture),
                ["position"] = position.ToString(CultureInfo.InvariantCulture)
            };

            var response = await client
                .PostMultipartAsync(UploadPath, fields, filePath, session.AccessToken, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccess || response.Document is null)
            {
                return PageUpload.Refused(
                    KindOf(response.Error),
                    response.Error?.Message ?? "Сайт не принял файл.");
            }

            using var document = response.Document;

            if (document.RootElement.TryGetProperty("corrupted_file", out var corrupted) &&
                corrupted.ValueKind == JsonValueKind.String)
            {
                return PageUpload.Refused(
                    UploadFailureKind.File,
                    "Сайт не смог прочитать файл: " +
                    (corrupted.GetString() ?? "файл повреждён") +
                    ". Проверьте фотографию.");
            }

            return PageUpload.Accepted;
        }

        /// <summary>The client's reason, in the words the upload screen switches on.</summary>
        private static UploadFailureKind KindOf(SiteApiError? error) => error?.Kind switch
        {
            SiteApiErrorKind.Unauthorized => UploadFailureKind.Unauthorized,
            SiteApiErrorKind.RateLimited => UploadFailureKind.RateLimited,
            SiteApiErrorKind.ServerUnavailable => UploadFailureKind.ServerUnavailable,
            SiteApiErrorKind.ConnectionLost or SiteApiErrorKind.Timeout => UploadFailureKind.ConnectionLost,
            SiteApiErrorKind.FileTooLarge => UploadFailureKind.File,
            SiteApiErrorKind.InvalidRequest or SiteApiErrorKind.NotFound => UploadFailureKind.Parameters,
            _ => UploadFailureKind.Other
        };
    }
}
