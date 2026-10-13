using PhotoBookRenamer.Presentation.ViewModels;
using System.Windows;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBookRenamer.Presentation.Views
{
    public partial class UpdateDialog : Window
    {
        public UpdateDialog(UpdateDialogViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}


