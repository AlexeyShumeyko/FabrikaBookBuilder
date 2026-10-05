using Microsoft.Extensions.DependencyInjection;
using PhotoBookRenamer.Application;

namespace PhotoBookRenamer.Infrastructure
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
            services.AddSingleton<IProjectRepository, ProjectRepository>();

            return services;
        }
    }
}
