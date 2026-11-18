using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Shell.Views
{
    /// <summary>
    /// The window that asks, shows the run and reports it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is a frame, not a screen: what it shows and what may happen are the view model's, and
    /// the window only turns clicks into its calls. That is why the same view model can be
    /// handed to it as is, and why closing it is the only decision the window makes itself.
    /// </para>
    /// <para>
    /// Closing says where the customer wants to be: <c>true</c> for the projects list once the
    /// order is on the site, <c>false</c> for the page the order was assembled on.
    /// </para>
    /// </remarks>
    public partial class UploadWindow : Window
    {
        public UploadWindow(SiteUploadViewModel screen)
        {
            InitializeComponent();

            DataContext = screen;
        }

        private void OnSendClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is SiteUploadViewModel screen)
                _ = screen.UploadAsync();
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e) =>
            (DataContext as SiteUploadViewModel)?.Cancel();

        private void OnOpenSiteClick(object? sender, RoutedEventArgs e)
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
            catch (Exception)
            {
                // The browser is a convenience; the project is already on the site, so the
                // window hands the customer the address instead of calling the run failed.
                screen.ReportLinkFailure();
            }
        }

        /// <summary>Leaves for the projects list, which is where a finished order belongs.</summary>
        private void OnProjectsClick(object? sender, RoutedEventArgs e) => Close(true);

        private void OnCloseClick(object? sender, RoutedEventArgs e) => Close(false);
    }
}
