using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Application.Sites;

namespace PhotoBook.Infrastructure.Sites
{
    /// <summary>
    /// The HTTP side of one region: a single API root, a bearer token per call, and the retry
    /// policy the platform can live with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client is built around one region and has no way to change it. That is not a
    /// limitation but the point: there is no code path that "tries the other site" when a call
    /// fails, so an order cannot end up on the wrong site by accident.
    /// </para>
    /// <para>
    /// Retries are limited to 429 and 5xx, with exponential delay and a cap of three attempts.
    /// A 401 is not retried: it means the session ended, and repeating it would only look like
    /// a password guess against the platform.
    /// </para>
    /// </remarks>
    internal sealed class SiteApiClient
    {
        private const int MaxAttempts = 3;
        private const string UserAgentProduct = "BookBuilderStudio";

        private readonly HttpClient _http;
        private readonly TimeSpan _retryDelay;

        public SiteApiClient(SiteRegion region, HttpClient http, TimeSpan? retryDelay = null)
        {
            Region = region ?? throw new ArgumentNullException(nameof(region));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(500);

            if (!_http.DefaultRequestHeaders.UserAgent.ToString().StartsWith(UserAgentProduct, StringComparison.Ordinal))
            {
                _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", BuildUserAgent());
            }
        }

        public SiteRegion Region { get; }

        /// <summary>
        /// The platform's own login endpoint, on the site's host rather than on the API host.
        /// </summary>
        public Task<SiteApiResult> SignInAsync(string email, string password, CancellationToken cancellationToken)
        {
            return SendWithRetryAsync(
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, Region.SiteUrl + "/api/auth/login");
                    request.Content = JsonContent.Create(new { email, password });
                    return request;
                },
                token: null,
                cancellationToken);
        }

        public Task<SiteApiResult> GetAsync(string path, string? token, CancellationToken cancellationToken)
        {
            return SendWithRetryAsync(
                () => NewRequest(HttpMethod.Get, ApiUrl(path), token),
                token,
                cancellationToken);
        }

        /// <summary>
        /// A GET on the site's own host rather than on the API host.
        /// </summary>
        /// <remarks>
        /// The calculator's price endpoint lives on the site host, next to the page that calls
        /// it, while the catalog and the projects live under the site's prefix of the shared API
        /// host. The two are different roots of the same region, and the price must be asked
        /// where the page asks it: the API host answers zero for combinations the site sells.
        /// </remarks>
        public Task<SiteApiResult> GetOnSiteAsync(string pathAndQuery, string? token, CancellationToken cancellationToken)
        {
            return SendWithRetryAsync(
                () => NewRequest(
                    HttpMethod.Get,
                    Region.SiteUrl + "/" + (pathAndQuery ?? string.Empty).TrimStart('/'),
                    token),
                token,
                cancellationToken);
        }

        public Task<SiteApiResult> PostJsonAsync<T>(string path, T body, string? token, CancellationToken cancellationToken)
        {
            return SendWithRetryAsync(
                () =>
                {
                    var request = NewRequest(HttpMethod.Post, ApiUrl(path), token);
                    request.Content = JsonContent.Create(body);
                    return request;
                },
                token,
                cancellationToken);
        }

        /// <summary>
        /// One multipart request with a file. Not retried, unlike the reads above: a page sent
        /// twice at one position may land as two pages, and a customer pressing the button
        /// again is a better answer than a silent repeat.
        /// </summary>
        public async Task<SiteApiResult> PostMultipartAsync(
            string path,
            IReadOnlyDictionary<string, string> fields,
            string filePath,
            string? token,
            CancellationToken cancellationToken)
        {
            using var request = NewRequest(HttpMethod.Post, ApiUrl(path), token);
            using var content = new MultipartFormDataContent();

            foreach (var field in fields)
                content.Add(new StringContent(field.Value), field.Key);

            var file = new StreamContent(System.IO.File.OpenRead(filePath));
            file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            content.Add(file, "file", System.IO.Path.GetFileName(filePath));

            request.Content = content;

            try
            {
                using var response = await _http
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                    .ConfigureAwait(false);

                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                return response.IsSuccessStatusCode
                    ? Parse(text)
                    : SiteApiResult.Fail(DescribeStatus(response.StatusCode));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                return SiteApiResult.Fail(new SiteApiError(
                    SiteApiErrorKind.ConnectionLost,
                    "Нет связи с сайтом. Проверьте интернет и попробуйте снова."));
            }
            catch (TaskCanceledException)
            {
                return SiteApiResult.Fail(new SiteApiError(
                    SiteApiErrorKind.Timeout,
                    "Сайт не ответил вовремя."));
            }
        }

        private string ApiUrl(string path) => $"{Region.ApiRoot}/{path.TrimStart('/')}";

        private HttpRequestMessage NewRequest(HttpMethod method, string url, string? token)
        {
            var request = new HttpRequestMessage(method, url);
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            return request;
        }

        private async Task<SiteApiResult> SendWithRetryAsync(
            Func<HttpRequestMessage> requestFactory,
            string? token,
            CancellationToken cancellationToken)
        {
            var lastError = new SiteApiError(SiteApiErrorKind.ConnectionLost, "Сайт не ответил.");

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var request = requestFactory();

                try
                {
                    using var response = await _http
                        .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                        .ConfigureAwait(false);

                    var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                        return Parse(text);

                    lastError = DescribeStatus(response.StatusCode);

                    if (!IsRetryable(response.StatusCode) || attempt == MaxAttempts)
                        return SiteApiResult.Fail(lastError);

                    await Task.Delay(RetryDelayFor(response, attempt), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (HttpRequestException exception)
                {
                    lastError = new SiteApiError(
                        SiteApiErrorKind.ConnectionLost,
                        "Нет связи с сайтом. Проверьте интернет и попробуйте снова.");
                    if (attempt == MaxAttempts)
                        return SiteApiResult.Fail(lastError);

                    await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    lastError = new SiteApiError(SiteApiErrorKind.Timeout, "Сайт не ответил вовремя.");
                    if (attempt == MaxAttempts)
                        return SiteApiResult.Fail(lastError);

                    await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                }
            }

            return SiteApiResult.Fail(lastError);
        }

        private static SiteApiResult Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return SiteApiResult.Fail(
                    SiteApiErrorKind.EmptyResponse, "Сайт вернул пустой ответ.");

            try
            {
                return SiteApiResult.Ok(JsonDocument.Parse(text));
            }
            catch (JsonException)
            {
                return SiteApiResult.Fail(
                    SiteApiErrorKind.MalformedResponse, "Сайт вернул ответ в неизвестном формате.");
            }
        }

        private static bool IsRetryable(HttpStatusCode status) =>
            status == HttpStatusCode.TooManyRequests || (int)status >= 500;

        private TimeSpan RetryDelayFor(HttpResponseMessage response, int attempt)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
                return delta > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delta;

            if (retryAfter?.Date is { } date)
            {
                var wait = date - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                    return wait > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : wait;
            }

            return Backoff(attempt);
        }

        private TimeSpan Backoff(int attempt) => _retryDelay * Math.Pow(2, attempt - 1);

        private static SiteApiError DescribeStatus(HttpStatusCode status)
        {
            var code = (int)status;

            return status switch
            {
                HttpStatusCode.Unauthorized => new SiteApiError(
                    SiteApiErrorKind.Unauthorized, "Нужно войти заново: сеанс истёк.", code),
                HttpStatusCode.Forbidden => new SiteApiError(
                    SiteApiErrorKind.Forbidden, "У аккаунта нет доступа к этому действию.", code),
                HttpStatusCode.NotFound => new SiteApiError(
                    SiteApiErrorKind.NotFound, "Сайт не нашёл запрошенные данные.", code),
                HttpStatusCode.UnprocessableEntity => new SiteApiError(
                    SiteApiErrorKind.InvalidRequest, "Сайт не принял параметры запроса.", code),
                HttpStatusCode.TooManyRequests => new SiteApiError(
                    SiteApiErrorKind.RateLimited, "Сайт просит подождать: слишком много запросов.", code),
                HttpStatusCode.RequestEntityTooLarge => new SiteApiError(
                    SiteApiErrorKind.FileTooLarge, "Файл слишком велик для загрузки на сайт.", code),
                _ when code >= 500 => new SiteApiError(
                    SiteApiErrorKind.ServerUnavailable, "Сайт временно недоступен. Попробуйте позже.", code),
                _ => new SiteApiError(
                    SiteApiErrorKind.Other, "Сайт ответил кодом " + code + ".", code)
            };
        }

        private static string BuildUserAgent()
        {
            var version = (Assembly.GetEntryAssembly() ?? typeof(SiteApiClient).Assembly)
                .GetName().Version;

            var number = version is null
                ? "2.0.0"
                : $"{version.Major}.{version.Minor}.{version.Build}";

            return $"{UserAgentProduct}/{number} (Windows; +https://github.com/AlexeyShumeyko/FabrikaBookBuilder)";
        }
    }
}
