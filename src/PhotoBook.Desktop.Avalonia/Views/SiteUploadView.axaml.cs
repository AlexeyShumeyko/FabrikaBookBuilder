using Avalonia.Controls;
using Avalonia.Interactivity;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Shell.Views
{
    /// <summary>
    /// The upload screen. Starting a run and leaving it belong to the view, because both are
    /// commands the customer gives; the screen itself decides what may happen, and says so by
    /// enabling the buttons.
    /// </summary>
    public partial class SiteUploadView : UserControl
    {
        public SiteUploadView() => InitializeComponent();

        private void OnUpload(object? sender, RoutedEventArgs e)
        {
            if (DataContext is SiteUploadViewModel screen)
                _ = screen.UploadAsync();
        }

        private void OnOpenSite(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not SiteUploadViewModel screen || !screen.CanOpenSite)
                return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = screen.OnSiteLink,
                    UseShellExecute = true
                });
            }
            catch (System.Exception)
            {
                // The browser is a convenience; the project is already on the site, so the
                // screen hands the customer the address instead of calling the run failed.
                screen.ReportLinkFailure();
            }
        }

        private void OnCancel(object? sender, RoutedEventArgs e) =>
            (DataContext as SiteUploadViewModel)?.Cancel();

        private void OnBack(object? sender, RoutedEventArgs e) =>
            (DataContext as SiteUploadViewModel)?.Back();

        private void OnProjects(object? sender, RoutedEventArgs e) =>
            (DataContext as SiteUploadViewModel)?.GoToProjects();
    }
}
