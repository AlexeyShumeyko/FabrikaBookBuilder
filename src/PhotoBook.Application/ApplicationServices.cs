using Microsoft.Extensions.DependencyInjection;
using PhotoBook.Application.Sites.Books.Files;
using PhotoBook.Application.Sites.Books.Pricing;
using PhotoBook.Application.Sites.Completion;
using PhotoBook.Application.Sites.Projects;
using PhotoBook.Application.Sites.Upload;

namespace PhotoBook.Application
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
            services.AddSingleton<IProjectBuilder, ProjectBuilder>();
            services.AddSingleton<IEditHistory, EditHistory>();
            services.AddSingleton<IProjectListService, ProjectListService>();
            services.AddSingleton<IBookFileInspector, BookFileInspector>();
            services.AddSingleton<IGroupQuoteService, GroupQuoteService>();
            services.AddSingleton<IProjectCreationService, ProjectCreationService>();
            services.AddSingleton<IBookUploadService, BookUploadService>();
            services.AddSingleton<IProjectCheckService, ProjectCheckService>();

            return services;
        }
    }
}
