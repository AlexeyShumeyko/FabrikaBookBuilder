using System;
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
    /// Reads the account's profile from the site's own user endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The session's region decides which site is asked: the same person can hold accounts on
    /// both sites, and an address from one of them means nothing on the other. Nothing here
    /// falls back to the other region, because that would answer with a different account's
    /// name.
    /// </para>
    /// <para>
    /// The name is resolved from what the site actually sends. A full name wins; otherwise a
    /// first name and a surname are joined; otherwise the address itself is the name, which is
    /// still true and never blank. The header has one line and must not show an empty one.
    /// </para>
    /// </remarks>
    internal sealed class SiteProfileService : ISiteProfileService
    {
        private const string ProfilePath = "auth/user";

        private readonly HttpClient _http;
        private readonly TimeSpan _retryDelay;

        public SiteProfileService(HttpClient http, TimeSpan? retryDelay = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(500);
        }

        public async Task<Result<SiteProfile>> GetAsync(
            SiteSession session,
            CancellationToken cancellationToken = default)
        {
            if (session is null)
                throw new ArgumentNullException(nameof(session));

            var client = new SiteApiClient(session.Region, _http, _retryDelay);
            var response = await client
                .GetAsync(ProfilePath, session.AccessToken, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccess || response.Document is null)
                return Result<SiteProfile>.Fail(response.Error?.Message ?? "Сайт не ответил профилем.");

            using var document = response.Document;
            var root = document.RootElement;

            var email = Text(root, "email") ?? session.AccountEmail ?? string.Empty;
            var name = ResolveName(root, email);

            if (name.Length == 0)
                return Result<SiteProfile>.Fail("Сайт не назвал имя аккаунта.");

            return Result<SiteProfile>.Ok(new SiteProfile(Id(root), name, email));
        }

        /// <summary>The account's name, from whichever of the site's fields is filled in.</summary>
        private static string ResolveName(JsonElement root, string email)
        {
            if (Text(root, "name") is { Length: > 0 } full)
                return full;

            var first = Text(root, "firstname") ?? string.Empty;
            var last = Text(root, "surname") ?? string.Empty;
            var joined = string.Join(" ", new[] { first, last }.Where(part => part.Length > 0));

            return joined.Length > 0 ? joined : email;
        }

        /// <summary>
        /// The account's id, which the site sends as a number but could send as a string.
        /// </summary>
        /// <remarks>
        /// Zero means "not said". Nothing in the program addresses an account by id yet, and a
        /// profile without one is still worth showing; pretending to have an id would be worse
        /// than admitting there is none.
        /// </remarks>
        private static long Id(JsonElement root)
        {
            if (!root.TryGetProperty("id", out var element))
                return 0;

            return element.ValueKind switch
            {
                JsonValueKind.Number when element.TryGetInt64(out var number) => number,
                JsonValueKind.String when long.TryParse(element.GetString(), out var parsed) => parsed,
                _ => 0
            };
        }

        private static string? Text(JsonElement root, string property)
        {
            if (!root.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.String)
                return null;

            var value = element.GetString()?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
