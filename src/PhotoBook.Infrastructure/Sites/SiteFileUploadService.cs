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
    /// <para>
    /// A page the project already holds is placed by <see cref="CopyAsync"/>, which names the
    /// stored picture and sends no file - the request the site's own uploader makes for a spread
    /// repeated in every book. The answer names the picture's number, so a page sent now can be
    /// copied for the pages after it.
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
            long projectItemId,
            int position,
            string filePath,
            int width = 0,
            int height = 0,
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

            var fields = Fields(projectId, projectItemId, position);
            fields["params[fileName]"] = System.IO.Path.GetFileName(filePath);
            fields["params[checksum]"] = string.Empty;

            if (width > 0)
                fields["params[fileWidth]"] = width.ToString(CultureInfo.InvariantCulture);

            if (height > 0)
                fields["params[fileHeight]"] = height.ToString(CultureInfo.InvariantCulture);

            var response = await client
                .PostMultipartAsync(UploadPath, fields, filePath, session.AccessToken, cancellationToken)
                .ConfigureAwait(false);

            return Read(response);
        }

        public async Task<PageUpload> CopyAsync(
            SiteSession session,
            long projectId,
            long projectItemId,
            int position,
            long imageId,
            string fileName,
            int width = 0,
            int height = 0,
            CancellationToken cancellationToken = default)
        {
            if (session is null)
                throw new ArgumentNullException(nameof(session));
            if (projectId <= 0)
                throw new ArgumentOutOfRangeException(nameof(projectId), "У проекта нет номера.");
            if (position < 0)
                throw new ArgumentOutOfRangeException(nameof(position), "Страница не может быть раньше первой.");
            if (imageId <= 0)
                return PageUpload.Refused(UploadFailureKind.Parameters, "Не названа страница для копирования.");

            var client = new SiteApiClient(session.Region, _http);

            var fields = Fields(projectId, projectItemId, position);
            fields["doublePageId"] = imageId.ToString(CultureInfo.InvariantCulture);

            if (!string.IsNullOrWhiteSpace(fileName))
                fields["params[fileName]"] = fileName;

            fields["params[checksum]"] = string.Empty;

            if (width > 0)
                fields["params[fileWidth]"] = width.ToString(CultureInfo.InvariantCulture);

            if (height > 0)
                fields["params[fileHeight]"] = height.ToString(CultureInfo.InvariantCulture);

            var response = await client
                .PostFieldsAsync(UploadPath, fields, session.AccessToken, cancellationToken)
                .ConfigureAwait(false);

            return Read(response);
        }

        /// <summary>The fields every upload of a page names: its project, its book and its place.</summary>
        private static Dictionary<string, string> Fields(long projectId, long projectItemId, int position)
        {
            var fields = new Dictionary<string, string>
            {
                ["projectId"] = projectId.ToString(CultureInfo.InvariantCulture),
                ["position"] = position.ToString(CultureInfo.InvariantCulture)
            };

            // Which book of the project the file belongs to. Without it the site puts every
            // file into the first book and replaces whatever already sits at that position, so
            // a project of three books would end up holding one.
            if (projectItemId > 0)
                fields["projectItemId"] = projectItemId.ToString(CultureInfo.InvariantCulture);

            return fields;
        }

        /// <summary>One answer from the uploader, whichever way the page was placed.</summary>
        private static PageUpload Read(SiteApiResult response)
        {
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

            var imageId = ImageIdOf(document.RootElement);
            return imageId > 0 ? PageUpload.AcceptedImage(imageId) : PageUpload.Accepted;
        }

        /// <summary>
        /// The number the site gave the stored picture.
        /// </summary>
        /// <remarks>
        /// The answer is <c>{"double_pages":[{"id":18954166,...}]}</c>, and that <c>id</c> is what
        /// the site's own uploader sends back as <c>doublePageId</c>. It arrives as a number here
        /// and was a string in the project document, so both are read.
        /// </remarks>
        private static long ImageIdOf(JsonElement root)
        {
            if (!root.TryGetProperty("double_pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
                return 0;

            foreach (var page in pages.EnumerateArray())
            {
                if (!page.TryGetProperty("id", out var id))
                    continue;

                if (id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var number))
                    return number;

                if (id.ValueKind == JsonValueKind.String &&
                    long.TryParse(id.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                    return number;
            }

            return 0;
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
