using Avalonia.Controls;
using Avalonia.Interactivity;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Shell.Views
{
    /// <summary>
    /// The order page. Going back is the view's own job because the page does not know which
    /// project it was opened from - the navigator does - and asking for the price is a command
    /// the customer gives rather than something the page does on its own.
    /// </summary>
    public partial class SiteOrderView : UserControl
    {
        public SiteOrderView() => InitializeComponent();

        private void OnBack(object? sender, RoutedEventArgs e) =>
            (DataContext as SiteOrderViewModel)?.Back();

        private void OnUpload(object? sender, RoutedEventArgs e) =>
            (DataContext as SiteOrderViewModel)?.Upload();

        private void OnRecalculate(object? sender, RoutedEventArgs e)
        {
            if (DataContext is SiteOrderViewModel page)
                _ = page.RecalculateAsync();
        }
    }
}
