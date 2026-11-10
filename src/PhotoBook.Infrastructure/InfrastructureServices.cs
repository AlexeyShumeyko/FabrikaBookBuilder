using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using PhotoBook.Application;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Catalog;
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
            services.AddSingleton<ISiteAuthService>(_ => new SiteAuthService(new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            }));

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

            // The catalog is fetched once and reused: the platform is shared by two sites and
            // must not be asked for the same option list whenever a window opens. What cannot be
            // cached is journalled, so a cache that stopped writing is visible rather than silent.
            services.AddSingleton<ISiteCatalogCache, FileSiteCatalogCache>();

            return services;
        }
    }
}
