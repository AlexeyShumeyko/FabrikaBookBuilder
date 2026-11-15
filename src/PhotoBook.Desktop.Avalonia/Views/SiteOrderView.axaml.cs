using Avalonia.Controls;
using Avalonia.Interactivity;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Shell.Views
{
    /// <summary>
    /// The order page. Going back is the view's own job because the page does not know which
    /// project it was opened from - the navigator does. The price is not a command of this view:
    /// the page asks for it itself once the answers are complete.
    /// </summary>
    public partial class SiteOrderView : UserControl
    {
        public SiteOrderView() => InitializeComponent();

        private void OnBack(object? sender, RoutedEventArgs e) =>
            (DataContext as SiteOrderViewModel)?.Back();

        private void OnUpload(object? sender, RoutedEventArgs e) =>
            (DataContext as SiteOrderViewModel)?.Upload();
    }
}
