using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
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
        public Task<Result<JsonDocument>> SignInAsync(string email, string password, CancellationToken cancellationToken)
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

        public Task<Result<JsonDocument>> GetAsync(string path, string? token, CancellationToken cancellationToken)
        {
            return SendWithRetryAsync(
                () => NewRequest(HttpMethod.Get, ApiUrl(path), token),
                token,
                cancellationToken);
        }

        public Task<Result<JsonDocument>> PostJsonAsync<T>(string path, T body, string? token, CancellationToken cancellationToken)
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

        private string ApiUrl(string path) => $"{Region.ApiRoot}/{path.TrimStart('/')}";

        private HttpRequestMessage NewRequest(HttpMethod method, string url, string? token)
        {
            var request = new HttpRequestMessage(method, url);
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            return request;
        }

        private async Task<Result<JsonDocument>> SendWithRetryAsync(
            Func<HttpRequestMessage> requestFactory,
            string? token,
            CancellationToken cancellationToken)
        {
            var lastError = "Сайт не ответил.";

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
                        return Result<JsonDocument>.Fail(lastError);

                    await Task.Delay(RetryDelayFor(response, attempt), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (HttpRequestException exception)
                {
                    lastError = "Нет связи с сайтом: " + exception.Message;
                    if (attempt == MaxAttempts)
                        return Result<JsonDocument>.Fail(lastError);

                    await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    lastError = "Сайт не ответил вовремя.";
                    if (attempt == MaxAttempts)
                        return Result<JsonDocument>.Fail(lastError);

                    await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                }
            }

            return Result<JsonDocument>.Fail(lastError);
        }

        private static Result<JsonDocument> Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Result<JsonDocument>.Fail("Сайт вернул пустой ответ.");

            try
            {
                return Result<JsonDocument>.Ok(JsonDocument.Parse(text));
            }
            catch (JsonException)
            {
                return Result<JsonDocument>.Fail("Сайт вернул ответ в неизвестном формате.");
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

        private static string DescribeStatus(HttpStatusCode status) => status switch
        {
            HttpStatusCode.Unauthorized => "Неверная почта или пароль.",
            HttpStatusCode.Forbidden => "У аккаунта нет доступа к этому действию.",
            HttpStatusCode.NotFound => "Сайт не нашёл запрошенные данные.",
            HttpStatusCode.TooManyRequests => "Сайт просит подождать: слишком много запросов.",
            HttpStatusCode.RequestEntityTooLarge => "Файл слишком велик для загрузки на сайт.",
            _ when (int)status >= 500 => "Сайт временно недоступен. Попробуйте позже.",
            _ => "Сайт ответил кодом " + (int)status + "."
        };

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
