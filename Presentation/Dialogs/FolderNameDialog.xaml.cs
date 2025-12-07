using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace PhotoBookRenamer.Presentation.Dialogs
{
    public partial class FolderNameDialog : Window
    {
        public string? SelectedPath { get; private set; }
        public string? FolderName { get; private set; }

        public FolderNameDialog(string? defaultPath = null, string? defaultFolderName = null)
        {
            InitializeComponent();
            
            if (!string.IsNullOrEmpty(defaultPath))
            {
                FolderPathTextBox.Text = defaultPath;
            }
            else
            {
                FolderPathTextBox.Text = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
            }
            
            // Генерируем уникальное имя папки
            var baseName = defaultFolderName ?? "PhotoBookExport";
            var uniqueName = GenerateUniqueFolderName(FolderPathTextBox.Text, baseName);
            FolderNameTextBox.Text = uniqueName;
            FolderNameTextBox.SelectAll();
        }

        private string GenerateUniqueFolderName(string parentPath, string baseName)
        {
            var fullPath = Path.Combine(parentPath, baseName);
            if (!Directory.Exists(fullPath))
            {
                return baseName;
            }

            int counter = 1;
            string newName;
            do
            {
                newName = $"{baseName}_{counter}";
                fullPath = Path.Combine(parentPath, newName);
                counter++;
            } while (Directory.Exists(fullPath) && counter < 1000);

            return newName;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Выберите родительскую папку для экспорта",
                SelectedPath = FolderPathTextBox.Text
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                FolderPathTextBox.Text = dialog.SelectedPath;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var parentPath = FolderPathTextBox.Text.Trim();
            var folderName = FolderNameTextBox.Text.Trim();

            if (string.IsNullOrEmpty(parentPath))
            {
                System.Windows.MessageBox.Show("Выберите родительскую папку!", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(folderName))
            {
                System.Windows.MessageBox.Show("Введите имя папки!", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Проверяем валидность имени папки
            var invalidChars = Path.GetInvalidFileNameChars();
            if (folderName.IndexOfAny(invalidChars) >= 0)
            {
                System.Windows.MessageBox.Show("Имя папки содержит недопустимые символы!", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedPath = parentPath;
            FolderName = folderName;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}

