using Microsoft.Extensions.DependencyInjection;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Presentation
{
    /// <summary>
    /// Assembles the view models, so that every shell registers the same objects the same way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The editors are singletons, and that is not a default: their editing state has to
    /// outlive a view swap, so switching to the project list and back must find the books,
    /// the selection and the undo history exactly as they were left. A second shell
    /// registering them differently would be a second set of bugs rather than a second
    /// appearance of the program.
    /// </para>
    /// <para>
    /// Nothing here registers a view, a window, or a shell-owned dependency. Those belong to
    /// whichever shell is running, and the view models cannot see them to register them -
    /// which is the arrangement the whole layer exists to make possible.
    /// </para>
    /// </remarks>
    public static class PresentationServices
    {
        public static IServiceCollection AddPhotoBookPresentation(this IServiceCollection services)
        {
            services.AddSingleton<UniqueFoldersViewModel>();
            services.AddSingleton<CombinedModeViewModel>();

            return services;
        }
    }
}
