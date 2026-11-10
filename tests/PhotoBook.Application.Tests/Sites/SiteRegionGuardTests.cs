using PhotoBook.Application.Sites;

namespace PhotoBook.Application.Tests.Sites
{
    /// <summary>
    /// An account belongs to one site, and an operation aimed at the other site stops before a
    /// request is made. This is the rule that keeps an order off the wrong site.
    /// </summary>
    public class SiteRegionGuardTests
    {
        [Fact]
        public void Allows_the_site_the_account_belongs_to()
        {
            var result = SiteRegionGuard.EnsureSameRegion(SiteRegion.By, SiteRegion.By);

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void Refuses_the_other_site()
        {
            var result = SiteRegionGuard.EnsureSameRegion(SiteRegion.Com, SiteRegion.By);

            Assert.False(result.IsSuccess);
            Assert.Contains("fabrika-fotoknigi.by", result.Error);
        }

        [Fact]
        public void Refuses_the_other_site_in_both_directions()
        {
            Assert.False(SiteRegionGuard.EnsureSameRegion(SiteRegion.By, SiteRegion.Com).IsSuccess);
        }
    }
}
