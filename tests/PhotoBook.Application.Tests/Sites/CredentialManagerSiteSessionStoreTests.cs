using System;
using PhotoBook.Application.Sites;
using PhotoBook.Infrastructure.Sites;

namespace PhotoBook.Application.Tests.Sites
{
    /// <summary>
    /// The store is checked against the real Credential Manager rather than a stand-in: the
    /// part that can be wrong here is the interop with Windows, and a fake would not exercise it.
    /// </summary>
    /// <remarks>
    /// Each test uses its own entry name and deletes it afterwards, so a run never leaves a
    /// token behind and never touches the entry the program itself uses.
    /// </remarks>
    public class CredentialManagerSiteSessionStoreTests
    {
        private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

        private static string FreshTarget() =>
            "FabrikaBookBuilder/tests/" + Guid.NewGuid().ToString("N");

        [Fact]
        public void KeepsAndForgetsASession()
        {
            if (!OperatingSystem.IsWindows())
                return;

            var target = FreshTarget();
            var store = new CredentialManagerSiteSessionStore(target, () => Now);
            var session = new SiteSession(SiteRegion.Com, "token-value", Now.AddDays(7));

            try
            {
                Assert.Null(store.Load());

                store.Save(session);
                var loaded = store.Load();

                Assert.NotNull(loaded);
                Assert.Equal(session.Region.Code, loaded!.Region.Code);
                Assert.Equal(session.AccessToken, loaded.AccessToken);
                Assert.Equal(session.ExpiresAt, loaded.ExpiresAt);

                store.Clear();
                Assert.Null(store.Load());
            }
            finally
            {
                store.Clear();
            }
        }

        [Fact]
        public void DoesNotOfferAnExpiredSessionBack()
        {
            if (!OperatingSystem.IsWindows())
                return;

            var target = FreshTarget();
            var store = new CredentialManagerSiteSessionStore(target, () => Now);
            var expired = new SiteSession(SiteRegion.Com, "old-token", Now.AddSeconds(-1));

            try
            {
                store.Save(expired);

                Assert.Null(store.Load());

                // The dead entry is removed, not left to be read on every start.
                Assert.Null(store.Load());
                Assert.Null(new CredentialManagerSiteSessionStore(target, () => Now.AddYears(-1)).Load());
            }
            finally
            {
                store.Clear();
            }
        }

        [Fact]
        public void ReplacesTheSessionItHad()
        {
            if (!OperatingSystem.IsWindows())
                return;

            var target = FreshTarget();
            var store = new CredentialManagerSiteSessionStore(target, () => Now);

            try
            {
                store.Save(new SiteSession(SiteRegion.Com, "first", Now.AddDays(7)));
                store.Save(new SiteSession(SiteRegion.By, "second", Now.AddDays(3)));

                var loaded = store.Load();

                Assert.NotNull(loaded);
                Assert.Equal("second", loaded!.AccessToken);
                Assert.Equal(SiteRegion.By.Code, loaded.Region.Code);
            }
            finally
            {
                store.Clear();
            }
        }
    }
}
