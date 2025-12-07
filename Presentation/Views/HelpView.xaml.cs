using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhotoBookRenamer.Presentation.ViewModels;

namespace PhotoBookRenamer.Presentation.Views
{
    public partial class HelpView : UserControl
    {
        public HelpView(HelpViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private void InnerScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}

