using Microsoft.Extensions.DependencyInjection;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Application.Sites.Pricing;
using PhotoBook.Infrastructure;
using PhotoBook.Infrastructure.Sites;
using PhotoBook.Infrastructure.Sites.Catalog;

namespace PhotoBook.Application.Tests.Sites
{
    /// <summary>
    /// The cache is registered by type, and its only required dependency is the journal, so the
    /// container has to be able to build it without anyone reaching into the provider.
    /// </summary>
    /// <remarks>
    /// A registration that cannot be resolved compiles and is found only when a window first
    /// asks for it, which is the worst moment to discover it.
    /// </remarks>
    public class SiteServicesTests
    {
        [Fact]
        public void ResolvesTheCatalogCacheFromTheContainer()
        {
            var services = new ServiceCollection();
            services.AddPhotoBookInfrastructure();

            using var provider = services.BuildServiceProvider();

            Assert.IsType<FileSiteCatalogCache>(provider.GetRequiredService<ISiteCatalogCache>());
            Assert.IsType<SiteAuthService>(provider.GetRequiredService<ISiteAuthService>());
            Assert.IsType<SitePriceService>(provider.GetRequiredService<ISitePriceService>());
        }
    }
}
