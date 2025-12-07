using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using PhotoBookRenamer.Application;
using PhotoBookRenamer.Infrastructure;

namespace PhotoBookRenamer.Presentation.Dialogs
{
    public class FolderInfo
    {
        public string Path { get; set; } = string.Empty;
        public int FileCount { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public partial class MultiFolderSelectionDialog : Window
    {
        private readonly IFileService _fileService;
        public ObservableCollection<FolderInfo> SelectedFolders { get; private set; } = new();
        private List<string> _existingFolders = new();

        public MultiFolderSelectionDialog(IFileService fileService, List<string>? existingFolders = null)
        {
            InitializeComponent();
            _fileService = fileService;
            
            if (existingFolders != null)
            {
                _existingFolders = existingFolders;
                foreach (var folder in existingFolders)
                {
                    SelectedFolders.Add(new FolderInfo { Path = folder, FileCount = 0 });
                }
            }
            
            FoldersListBox.ItemsSource = SelectedFolders;
            
            // Загружаем количество файлов для существующих папок
            _ = LoadFileCountsAsync();
        }

        private async Task LoadFileCountsAsync()
        {
            foreach (var folder in SelectedFolders)
            {
                try
                {
                    var files = await _fileService.GetJpegFilesAsync(folder.Path);
                    folder.FileCount = files.Count;
                }
                catch
                {
                    folder.FileCount = 0;
                }
            }
        }

        private void AddFolderButton_Click(object sender, RoutedEventArgs e)
        {
            // Позволяем выбрать несколько папок через цикл
            var folders = new List<string>();
            
            while (true)
            {
                using var dialog = new FolderBrowserDialog
                {
                    Description = folders.Count == 0 
                        ? "Выберите папку с фотографиями (можно выбрать несколько)" 
                        : $"Выбрано папок: {folders.Count}. Выберите следующую (Отмена для завершения)",
                    UseDescriptionForTitle = true
                };

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    if (!folders.Contains(dialog.SelectedPath) && 
                        !SelectedFolders.Any(f => f.Path == dialog.SelectedPath))
                    {
                        folders.Add(dialog.SelectedPath);
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

            foreach (var folder in folders)
            {
                AddFolder(folder);
            }
        }

        private async void AddFolder(string folderPath)
        {
            if (SelectedFolders.Any(f => f.Path == folderPath))
            {
                System.Windows.MessageBox.Show("Эта папка уже добавлена!", "Предупреждение", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var folderInfo = new FolderInfo { Path = folderPath };
            SelectedFolders.Add(folderInfo);

            // Загружаем количество файлов
            try
            {
                var files = await _fileService.GetJpegFilesAsync(folderPath);
                folderInfo.FileCount = files.Count;
            }
            catch
            {
                folderInfo.FileCount = 0;
            }

            // Валидируем в реальном времени
            _ = ValidateFoldersAsync();
        }

        private async void RemoveFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = FoldersListBox.SelectedItems.Cast<FolderInfo>().ToList();
            foreach (var item in selectedItems)
            {
                SelectedFolders.Remove(item);
            }
            
            await ValidateFoldersAsync();
        }

        private async Task ValidateFoldersAsync()
        {
            ErrorTextBlock.Text = string.Empty;
            OkButton.IsEnabled = true;

            if (SelectedFolders.Count == 0)
            {
                return;
            }

            var folderPaths = SelectedFolders.Select(f => f.Path).ToList();
            var validationResult = await _fileService.ValidateFoldersDetailedAsync(folderPaths);

            if (!validationResult.IsValid)
            {
                ErrorTextBlock.Text = validationResult.ErrorMessage ?? "Ошибка валидации папок.";
                
                // Подсвечиваем проблемную папку
                if (!string.IsNullOrEmpty(validationResult.ProblemFolder))
                {
                    var problemFolder = SelectedFolders.FirstOrDefault(f => f.Path == validationResult.ProblemFolder);
                    if (problemFolder != null)
                    {
                        problemFolder.ErrorMessage = validationResult.ErrorMessage;
                    }
                }
                
                OkButton.IsEnabled = false;
            }
            else
            {
                // Очищаем ошибки
                foreach (var folder in SelectedFolders)
                {
                    folder.ErrorMessage = null;
                }
            }
        }

        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFolders.Count == 0)
            {
                System.Windows.MessageBox.Show("Выберите хотя бы одну папку!", "Ошибка", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // Финальная валидация
            var folderPaths = SelectedFolders.Select(f => f.Path).ToList();
            var validationResult = await _fileService.ValidateFoldersDetailedAsync(folderPaths);

            if (!validationResult.IsValid)
            {
                ErrorTextBlock.Text = validationResult.ErrorMessage ?? "Ошибка валидации папок.";
                System.Windows.MessageBox.Show(
                    $"Не удалось загрузить папки:\n\n{validationResult.ErrorMessage}\n\nПожалуйста, исправьте ошибки и попробуйте снова.",
                    "Ошибка валидации",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        public List<string> GetSelectedFolderPaths()
        {
            return SelectedFolders.Select(f => f.Path).ToList();
        }
    }
}
