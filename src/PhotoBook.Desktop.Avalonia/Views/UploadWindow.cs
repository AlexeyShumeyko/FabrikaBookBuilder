using Avalonia.Controls;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Shell.Views
{
    /// <summary>
    /// The upload as a window over the order page: the customer confirms, watches the progress
    /// and reads the result without losing the page the order was assembled on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window is a frame, not a second screen: what it shows is the same upload screen the
    /// program had as a page, with its own button that starts the only write the program makes.
    /// Opening the window is the confirmation, because the customer asked for it by pressing
    /// «Загрузить на сайт» with the plan in front of them.
    /// </para>
    /// <para>
    /// Closing it says where the customer wants to be next: <c>true</c> for the projects list
    /// after a finished run, <c>false</c> for the order page they came from - the same order,
    /// with whatever the site already holds still written down.
    /// </para>
    /// </remarks>
    public sealed class UploadWindow : Window
    {
        public UploadWindow(SiteUploadViewModel screen)
        {
            Title = "Отправка заказа на сайт";
            Width = 1040;
            Height = 780;
            MinWidth = 880;
            MinHeight = 600;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Content = new SiteUploadView { DataContext = screen };

            screen.BackRequested += (_, _) => Close(false);
            screen.ProjectsRequested += (_, _) => Close(true);
        }
    }
}
