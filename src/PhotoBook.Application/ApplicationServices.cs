using Microsoft.Extensions.DependencyInjection;

namespace PhotoBookRenamer.Application
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
    /// either a cache (photo headers, thumbnails) or the record of what the user has open.
    /// Two instances would mean two answers to the same question.
    /// </para>
    /// </remarks>
    public static class ApplicationServices
    {
        public static IServiceCollection AddPhotoBookApplication(this IServiceCollection services)
        {
            services.AddSingleton<IExportService, ExportService>();
            services.AddSingleton<IProjectService, ProjectService>();
            services.AddSingleton<IProjectListService, ProjectListService>();

            return services;
        }
    }
}
