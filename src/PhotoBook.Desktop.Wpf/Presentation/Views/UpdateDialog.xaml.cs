using System.Windows;
using PhotoBookRenamer.Presentation.ViewModels;

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


