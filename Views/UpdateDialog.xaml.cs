using System.Windows;
using PhotoBookRenamer.ViewModels;

namespace PhotoBookRenamer.Views
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

