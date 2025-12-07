using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Forms;

namespace PhotoBookRenamer.Presentation.Dialogs
{
    public partial class MultiFolderPickerDialog : Window
    {
        public List<string> SelectedFolders { get; private set; } = new();

        public MultiFolderPickerDialog(List<string>? existingFolders = null)
        {
            InitializeComponent();
            
            if (existingFolders != null && existingFolders.Count > 0)
            {
                foreach (var folder in existingFolders)
                {
                    SelectedFolders.Add(folder);
                }
            }
            
            FoldersListBox.ItemsSource = SelectedFolders;
            UpdateOkButton();
        }

        private void SelectFoldersButton_Click(object sender, RoutedEventArgs e)
        {
            // Используем цикл для выбора нескольких папок
            var newFolders = new List<string>();
            
            while (true)
            {
                using var dialog = new FolderBrowserDialog
                {
                    Description = newFolders.Count == 0 
                        ? "Выберите папку с фотографиями" 
                        : $"Выбрано папок: {newFolders.Count}. Выберите следующую (Отмена для завершения)",
                    UseDescriptionForTitle = true
                };

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var selectedPath = dialog.SelectedPath;
                    
                    // Проверяем, не добавлена ли уже эта папка
                    if (!newFolders.Contains(selectedPath) && !SelectedFolders.Contains(selectedPath))
                    {
                        newFolders.Add(selectedPath);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Эта папка уже добавлена!", "Предупреждение", 
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    }
                }
                else
                {
                    break;
                }
            }

            // Добавляем новые папки к списку
            foreach (var folder in newFolders)
            {
                if (!SelectedFolders.Contains(folder))
                {
                    SelectedFolders.Add(folder);
                }
            }

            // Обновляем список
            FoldersListBox.ItemsSource = null;
            FoldersListBox.ItemsSource = SelectedFolders;
            UpdateOkButton();
        }

        private void UpdateOkButton()
        {
            OkButton.IsEnabled = SelectedFolders.Count > 0;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFolders.Count == 0)
            {
                System.Windows.MessageBox.Show("Выберите хотя бы одну папку!", "Ошибка", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}













