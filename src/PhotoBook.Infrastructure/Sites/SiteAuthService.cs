using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Application.Sites;

namespace PhotoBook.Infrastructure.Sites
{
    /// <summary>
    /// Signs in on whichever site owns the account.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both sites are asked at the same time, one request each. Doing it in sequence would make
    /// a customer wait for the first site to fail before the second was even tried, and would
    /// double the time a wrong password takes to be reported.
    /// </para>
    /// <para>
    /// When both sites accept the password the account exists in both places. The customer
    /// orders on one of them, and the program must not pick by chance: it looks at where the
    /// account has projects. If neither site has any, the first region in the registry wins,
    /// which is stable across runs.
    /// </para>
    /// </remarks>
    internal sealed class SiteAuthService : ISiteAuthService
    {
        private readonly HttpClient _http;
        private readonly Func<DateTimeOffset> _now;
        private readonly TimeSpan _retryDelay;

        public SiteAuthService(HttpClient http, Func<DateTimeOffset>? now = null, TimeSpan? retryDelay = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _now = now ?? (() => DateTimeOffset.UtcNow);
            _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(500);
        }

        public async Task<Result<SiteSession>> SignInAsync(
            string email,
            string password,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
                return Result<SiteSession>.Fail("Введите почту и пароль.");

            var attempts = await Task
                .WhenAll(SiteRegion.All.Select(region => TrySignInAsync(region, email, password, cancellationToken)))
                .ConfigureAwait(false);

            var signedIn = attempts
                .Where(attempt => attempt.Session is not null)
                .Select(attempt => attempt.Session!)
                .ToList();

            if (signedIn.Count == 0)
                return Result<SiteSession>.Fail(Describe(attempts));

            if (signedIn.Count == 1)
                return Result<SiteSession>.Ok(signedIn[0]);

            foreach (var session in signedIn)
            {
                if (await HasProjectsAsync(session, cancellationToken).ConfigureAwait(false))
                    return Result<SiteSession>.Ok(session);
            }

            return Result<SiteSession>.Ok(signedIn[0]);
        }

        private async Task<Attempt> TrySignInAsync(
            SiteRegion region,
            string email,
            string password,
            CancellationToken cancellationToken)
        {
            var client = new SiteApiClient(region, _http, _retryDelay);

            var response = await client.SignInAsync(email, password, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess || response.Value is null)
                return new Attempt(region, null, response.Error);

            using var document = response.Value;

            if (!document.RootElement.TryGetProperty("access_token", out var tokenElement) ||
                tokenElement.ValueKind != JsonValueKind.String)
                return new Attempt(region, null, "Сайт не выдал токен доступа.");

            var lifetimeSeconds = document.RootElement.TryGetProperty("expires_in", out var expiresElement) &&
                                  expiresElement.TryGetInt64(out var seconds) &&
                                  seconds > 0
                ? seconds
                : 7 * 24 * 60 * 60;

            var session = new SiteSession(
                region,
                tokenElement.GetString()!,
                _now().AddSeconds(lifetimeSeconds));

            return new Attempt(region, session, null);
        }

        /// <summary>
        /// Whether the account holds any work on this site.
        /// </summary>
        /// <remarks>
        /// Only used to break the tie when both sites accept the password. A failure here is not
        /// a reason to refuse the sign-in: the account simply looks unused, and the caller still
        /// gets a session.
        /// </remarks>
        private async Task<bool> HasProjectsAsync(SiteSession session, CancellationToken cancellationToken)
        {
            var client = new SiteApiClient(session.Region, _http, _retryDelay);
            var response = await client
                .GetAsync("creators/projects?with_update_price=1", session.AccessToken, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccess || response.Value is null)
                return false;

            using var document = response.Value;

            return document.RootElement.TryGetProperty("data", out var data) &&
                   data.ValueKind == JsonValueKind.Array &&
                   data.GetArrayLength() > 0;
        }

        private static string Describe(IReadOnlyList<Attempt> attempts)
        {
            var first = attempts
                .Select(attempt => attempt.Error)
                .FirstOrDefault(error => !string.IsNullOrWhiteSpace(error));

            return first ?? "Не удалось войти. Попробуйте позже.";
        }

        private sealed record Attempt(SiteRegion Region, SiteSession? Session, string? Error);
    }
}
