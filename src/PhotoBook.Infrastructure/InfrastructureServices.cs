using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using PhotoBook.Application;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Application.Sites.Pricing;
using PhotoBook.Application.Sites.Projects;
using PhotoBook.Application.Sites.Upload;
using PhotoBook.Infrastructure.Sites;
using PhotoBook.Infrastructure.Sites.Catalog;

namespace PhotoBook.Infrastructure
{
    /// <summary>
    /// How this layer is put together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The layer assembles itself rather than listing its implementations somewhere else,
    /// because it is the only place that knows what implements its ports. The alternative -
    /// the composition root naming concrete classes - is what this arrangement removes: the
    /// implementations are internal, so a view or a dialog that tries to construct one does
    /// not compile.
    /// </para>
    /// <para>
    /// Everything registered here is a singleton because the state these objects carry is
    /// either a cache (photo headers, thumbnails) or a resource that must not be opened twice
    /// (the HTTP client behind the release feed).
    /// </para>
    /// </remarks>
    public static class InfrastructureServices
    {
        public static IServiceCollection AddPhotoBookInfrastructure(this IServiceCollection services)
        {
            services.AddSingleton<IFileService, FileService>();
            services.AddSingleton<IImageService, ImageService>();
            services.AddSingleton<IThumbnailProvider, ThumbnailProvider>();
            services.AddSingleton<IUpdateFeed, UpdateService>();
            services.AddSingleton<ILoggingService, LoggingService>();

            // The journal is in the container rather than reached for statically, so that anything
            // with a reason to write something down can be given it. A converter with no journal
            // writes nothing, which is how a photograph that cannot be read used to leave no trace.
            services.AddSingleton<IJournal>(sp => new FileJournal(Diagnostics.Directory));
            services.AddSingleton<IProjectRepository, ProjectRepository>();
            services.AddSingleton<IProjectIndexStore, ProjectIndexStore>();

            // One HTTP client for the whole program. The platform is shared by the two sites and
            // the connection pool is the only thing that keeps repeated calls cheap; a client per
            // request would open a socket each time and look like load from the outside.
            services.AddSingleton(_ => new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            });
            services.AddSingleton<ISiteAuthService, SiteAuthService>();

            // The profile is read from the site the session belongs to, so the header can name
            // the person rather than an address, and both sites can hold different accounts.
            services.AddSingleton<ISiteProfileService, SiteProfileService>();

            // Prices are asked where the calculator page asks them: on the site host, with the
            // same query the browser sends. The API host answers zero for combinations the site
            // sells, so it cannot be used to tell a refusal from a price.
            services.AddSingleton<ISitePriceService, SitePriceService>();

            // The session outlives the process: the platform's token lasts seven days, and a
            // customer who closed the program should not type the password again. The Credential
            // Manager is the only place the token is written to.
            services.AddSingleton<ISiteSessionStore>(_ => new CredentialManagerSiteSessionStore());

            // The catalog is read from the document the site's own calculator reads, so the
            // product's ids, formats, options and quantity ranges are the site's rather than
            // this program's guesses.
            services.AddSingleton<ISiteProductParser, SiteProductParser>();

            // The uploader's geometry comes from its own document, including the resolution: the
            // file size a book needs is the site's number, not one compiled in here.
            services.AddSingleton<ISiteLayoutParser, SiteLayoutParser>();

            // Projects are created on the site once per group and remembered by their key, so a
            // retry after a dropped connection does not make a second project for the same books.
            services.AddSingleton<ISiteProjectService, SiteProjectService>();
            services.AddSingleton<ICreatedProjectStore>(sp => new FileCreatedProjectStore(sp.GetRequiredService<IJournal>()));

            // Files go up one page at a time through the same uploader endpoint the site's own
            // page uses; the answer's body decides success, not the status code.
            services.AddSingleton<ISiteFileUploadService, SiteFileUploadService>();

            // How far each book got survives a restart, so a dropped connection continues at the
            // next page instead of sending ten spreads over the ones the site already holds.
            services.AddSingleton<IAcceptedFilesStore>(sp => new FileAcceptedFilesStore(sp.GetRequiredService<IJournal>()));

            // The catalog is fetched once and reused: the platform is shared by two sites and
            // must not be asked for the same option list whenever a window opens. What cannot be
            // cached is journalled, so a cache that stopped writing is visible rather than silent.
            services.AddSingleton<ISiteCatalogCache, FileSiteCatalogCache>();

            return services;
        }
    }
}
