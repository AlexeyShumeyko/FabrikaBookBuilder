using System;
using PhotoBook.Application.Sites;

namespace PhotoBook.Application.Tests.Sites
{
    /// <summary>
    /// What is written to disk has to come back as the same session, and what the platform would
    /// already refuse has to come back as nothing rather than as a live session.
    /// </summary>
    public class SiteSessionTicketTests
    {
        private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

        private static SiteSession Session(SiteRegion? region = null, DateTimeOffset? expiresAt = null) =>
            new(
                region ?? SiteRegion.Com,
                "token.with-dots.and-lines",
                expiresAt ?? Now.AddDays(7),
                "customer@example.com");

        [Fact]
        public void ReadsBackTheSessionItWrote()
        {
            var session = Session();

            var read = SiteSessionTicket.Read(SiteSessionTicket.Encode(session), Now);

            Assert.True(read.IsSuccess, read.Error);
            Assert.Equal(session.Region.Code, read.Value!.Region.Code);
            Assert.Equal(session.AccessToken, read.Value.AccessToken);
            Assert.Equal(session.ExpiresAt, read.Value.ExpiresAt);
            Assert.Equal(session.AccountEmail, read.Value.AccountEmail);
        }

        [Fact]
        public void KeepsTheBySiteAsTheBySite()
        {
            var read = SiteSessionTicket.Read(
                SiteSessionTicket.Encode(Session(SiteRegion.By)),
                Now);

            Assert.True(read.IsSuccess, read.Error);
            Assert.Equal(SiteRegion.By.Code, read.Value!.Region.Code);
            Assert.Equal(SiteRegion.By.ShowcaseSlug, read.Value.Region.ShowcaseSlug);
        }

        [Fact]
        public void RefusesASessionThatExpired()
        {
            var payload = SiteSessionTicket.Encode(Session(expiresAt: Now.AddMinutes(-1)));

            var read = SiteSessionTicket.Read(payload, Now);

            Assert.False(read.IsSuccess);
            Assert.Equal("Срок действия сессии истёк — войдите снова.", read.Error);
        }

        [Fact]
        public void RefusesASessionWhoseSiteIsUnknown()
        {
            var payload = SiteSessionTicket.Encode(Session()).Replace("\"com\"", "\"kz\"");

            var read = SiteSessionTicket.Read(payload, Now);

            Assert.False(read.IsSuccess);
            Assert.Contains("неизвестному сайту", read.Error);
        }

        [Fact]
        public void RefusesTextThatIsNotATicket()
        {
            var read = SiteSessionTicket.Read("{not json", Now);

            Assert.False(read.IsSuccess);
            Assert.Contains("повреждена", read.Error);
        }

        [Fact]
        public void RefusesAnEmptyTicket()
        {
            var read = SiteSessionTicket.Read(null, Now);

            Assert.False(read.IsSuccess);
            Assert.Equal("Сохранённой сессии нет.", read.Error);
        }
    }
}
