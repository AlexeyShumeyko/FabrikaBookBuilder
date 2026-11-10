using System;
using System.Linq;
using System.Text.Json;

namespace PhotoBook.Application.Sites
{
    /// <summary>
    /// The text a stored session is kept as, and the check that it may still be used.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The store keeps bytes, not a session; the meaning of those bytes lives here, so both the
    /// expiry rule and the region rule are decided in one place and can be tested without a
    /// credential store.
    /// </para>
    /// <para>
    /// The region is written by code rather than index, because
    /// <see cref="SiteRegion.All"/> is the order sign-in tries sites in and may change: a stored
    /// "1" would silently point at another site after such a change, and the token would be
    /// used against the wrong one.
    /// </para>
    /// </remarks>
    public static class SiteSessionTicket
    {
        public static string Encode(SiteSession session)
        {
            if (session is null)
                throw new ArgumentNullException(nameof(session));

            return JsonSerializer.Serialize(
                new Ticket(session.Region.Code, session.AccessToken, session.ExpiresAt));
        }

        /// <summary>
        /// Reads a stored session, or explains why it cannot be used.
        /// </summary>
        /// <param name="payload">The text <see cref="Encode"/> produced.</param>
        /// <param name="now">The current moment, so an expired token is refused.</param>
        public static Result<SiteSession> Read(string? payload, DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return Result<SiteSession>.Fail("Сохранённой сессии нет.");

            Ticket? ticket;
            try
            {
                ticket = JsonSerializer.Deserialize<Ticket>(payload);
            }
            catch (JsonException)
            {
                return Result<SiteSession>.Fail(
                    "Сохранённая сессия повреждена — войдите снова.");
            }

            if (ticket is null || string.IsNullOrWhiteSpace(ticket.Token))
            {
                return Result<SiteSession>.Fail(
                    "Сохранённая сессия повреждена — войдите снова.");
            }

            var region = SiteRegion.All.FirstOrDefault(candidate =>
                string.Equals(candidate.Code, ticket.Region, StringComparison.Ordinal));

            if (region is null)
            {
                return Result<SiteSession>.Fail(
                    "Сохранённая сессия относится к неизвестному сайту — войдите снова.");
            }

            var session = new SiteSession(region, ticket.Token, ticket.ExpiresAt);

            return session.IsExpiredAt(now)
                ? Result<SiteSession>.Fail("Срок действия сессии истёк — войдите снова.")
                : Result<SiteSession>.Ok(session);
        }

        private sealed record Ticket(string Region, string Token, DateTimeOffset ExpiresAt);
    }
}
