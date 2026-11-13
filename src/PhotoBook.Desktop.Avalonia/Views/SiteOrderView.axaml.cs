using Avalonia.Controls;
using Avalonia.Interactivity;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Shell.Views
{
    /// <summary>
    /// The order page. Going back is the one thing the view asks the view model for, because
    /// the page does not know which project it was opened from - the navigator does.
    /// </summary>
    public partial class SiteOrderView : UserControl
    {
        public SiteOrderView() => InitializeComponent();

        private void OnBack(object? sender, RoutedEventArgs e) =>
            (DataContext as SiteOrderViewModel)?.Back();
    }
}
