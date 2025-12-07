using System.Windows;
using System.Windows.Input;

namespace PhotoBookRenamer.Utils
{
    public partial class ProjectNameDialog : Window
    {
        public string ProjectName { get; set; } = string.Empty;

        public ProjectNameDialog()
        {
            InitializeComponent();
            DataContext = this;
            ProjectNameTextBox.Focus();
            ProjectNameTextBox.SelectAll();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(ProjectName))
            {
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Введите название проекта!", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ProjectNameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OkButton_Click(sender, e);
            }
        }
    }
}










