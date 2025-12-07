using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhotoBookRenamer.Application;
using PhotoBookRenamer.Infrastructure;

namespace PhotoBookRenamer.Presentation.Dialogs
{
    public class FolderNode
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public List<FolderNode> Children { get; set; } = new();
        public bool IsExpanded { get; set; }
    }

    public class SelectedFolderInfo : INotifyPropertyChanged
    {
        private string _path = string.Empty;
        private bool _hasError;
        private string? _errorMessage;

        public string Path
        {
            get => _path;
            set
            {
                _path = value;
                OnPropertyChanged(nameof(Path));
            }
        }

        public bool HasError
        {
            get => _hasError;
            set
            {
                _hasError = value;
                OnPropertyChanged(nameof(HasError));
            }
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            set
            {
                _errorMessage = value;
                OnPropertyChanged(nameof(ErrorMessage));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class MultiFolderBrowserDialog : Window
    {
        private readonly IFileService _fileService;
        private readonly List<string> _selectedFolders = new();
        private readonly ObservableCollection<SelectedFolderInfo> _selectedFoldersInfo = new();
        private string _currentPath = string.Empty;
        private readonly Stack<string> _navigationHistory = new();

        public List<string> SelectedFolders => _selectedFolders;

        public MultiFolderBrowserDialog(IFileService fileService, List<string>? existingFolders = null)
        {
            InitializeComponent();
            _fileService = fileService;
            
            if (existingFolders != null)
            {
                foreach (var folder in existingFolders)
                {
                    _selectedFolders.Add(folder);
                    _selectedFoldersInfo.Add(new SelectedFolderInfo { Path = folder, HasError = false });
                }
            }
            
            UpdateSelectedFoldersList();
            LoadDrives();
            
            // Валидируем существующие папки при загрузке только если есть папки
            if (_selectedFolders.Count > 0)
            {
                _ = ValidateFoldersAsync();
            }
        }

        private void LoadDrives()
        {
            var drives = new List<FolderNode>
            {
                new FolderNode
                {
                    Name = "Рабочий стол",
                    FullPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                },
                new FolderNode
                {
                    Name = "Документы",
                    FullPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                },
                new FolderNode
                {
                    Name = "Изображения",
                    FullPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
                },
                new FolderNode
                {
                    Name = "Загрузки",
                    FullPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                }
            };

            drives.AddRange(DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .Select(d => new FolderNode
                {
                    Name = $"{d.Name} ({d.VolumeLabel})",
                    FullPath = d.RootDirectory.FullName
                }));

            NavigationTree.ItemsSource = drives;
            _currentPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            CurrentPathTextBlock.Text = _currentPath;
            LoadFolders(_currentPath);
        }

        private void NavigationTree_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavigationTree.SelectedItem is FolderNode node && !string.IsNullOrEmpty(node.FullPath))
            {
                try
                {
                    LoadFolders(node.FullPath);
                }
                catch
                {
                    // Игнорируем ошибки
                }
            }
        }

        private void LoadFolders(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                {
                    FoldersListBox.ItemsSource = new List<FolderNode>();
                    if (CurrentPathTextBlock != null)
                    {
                        CurrentPathTextBlock.Text = string.Empty;
                    }
                    return;
                }

                // Сохраняем текущий путь в историю перед переходом
                if (!string.IsNullOrEmpty(_currentPath) && _currentPath != path)
                {
                    _navigationHistory.Push(_currentPath);
                }

                _currentPath = path;
                if (CurrentPathTextBlock != null)
                {
                    CurrentPathTextBlock.Text = path;
                }
                if (BackButton != null)
                {
                    BackButton.IsEnabled = _navigationHistory.Count > 0;
                }
                
                var folders = new List<FolderNode>();
                
                try
                {
                    var dirs = Directory.GetDirectories(path)
                        .Where(d => 
                        {
                            try
                            {
                                // Проверяем доступность папки
                                var test = Directory.GetDirectories(d);
                                return true;
                            }
                            catch
                            {
                                return false;
                            }
                        })
                        .Select(d => new FolderNode
                        {
                            Name = Path.GetFileName(d),
                            FullPath = d
                        })
                        .OrderBy(d => d.Name)
                        .ToList();
                    
                    folders.AddRange(dirs);
                }
                catch (UnauthorizedAccessException)
                {
                    // Нет доступа к папке
                    folders.Clear();
                }
                catch (Exception ex)
                {
                    // Другие ошибки
                    MessageBox.Show($"Ошибка доступа к папке: {ex.Message}", "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    folders.Clear();
                }

                // Всегда устанавливаем источник данных, даже если список пуст
                FoldersListBox.ItemsSource = folders;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки папок: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                FoldersListBox.ItemsSource = new List<FolderNode>();
            }
        }

        private void FoldersListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FoldersListBox.SelectedItem is FolderNode node && !string.IsNullOrEmpty(node.FullPath))
            {
                try
                {
                    // Открываем папку и показываем её содержимое
                    LoadFolders(node.FullPath);
                    
                    // Обновляем навигацию - добавляем текущую папку в историю или обновляем выделение
                    // Можно также добавить кнопку "Назад" для навигации
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось открыть папку: {ex.Message}", "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void FoldersListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Можно добавить предпросмотр или другую логику при выборе
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_navigationHistory.Count > 0)
            {
                var previousPath = _navigationHistory.Pop();
                LoadFolders(previousPath);
                // Убираем текущий путь из истории, так как мы вернулись назад
                if (_navigationHistory.Count > 0 && _navigationHistory.Peek() == _currentPath)
                {
                    _navigationHistory.Pop();
                }
            }
        }

        private async void AddSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = FoldersListBox.SelectedItems.Cast<FolderNode>().ToList();
            
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Выберите папки для добавления! Используйте Ctrl+ЛКМ или Shift+ЛКМ для выбора нескольких папок.", "Предупреждение", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var addedCount = 0;
            foreach (var item in selectedItems)
            {
                if (!_selectedFolders.Contains(item.FullPath))
                {
                    _selectedFolders.Add(item.FullPath);
                    _selectedFoldersInfo.Add(new SelectedFolderInfo { Path = item.FullPath, HasError = false });
                    addedCount++;
                }
            }

            if (addedCount > 0)
            {
                UpdateSelectedFoldersList();
                FoldersListBox.SelectedItems.Clear();
                await ValidateFoldersAsync();
            }
            else
            {
                MessageBox.Show("Все выбранные папки уже добавлены!", "Информация", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void UpdateSelectedFoldersList()
        {
            SelectedFoldersListBox.ItemsSource = null;
            SelectedFoldersListBox.ItemsSource = _selectedFoldersInfo;
            OkButton.IsEnabled = _selectedFolders.Count > 0;
            
            // Обновляем состояние кнопки удалить
            UpdateRemoveButtonState();
        }

        private void SelectedFoldersListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateRemoveButtonState();
        }

        private void UpdateRemoveButtonState()
        {
            RemoveSelectedButton.IsEnabled = SelectedFoldersListBox.SelectedItems.Count > 0;
        }

        private async void RemoveSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFoldersListBox.SelectedItems.Count == 0)
            {
                MessageBox.Show("Выберите папки для удаления!", "Предупреждение", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedItems = SelectedFoldersListBox.SelectedItems.Cast<SelectedFolderInfo>().ToList();
            
            foreach (var item in selectedItems)
            {
                _selectedFolders.Remove(item.Path);
                _selectedFoldersInfo.Remove(item);
            }

            UpdateSelectedFoldersList();
            await ValidateFoldersAsync();
        }

        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFolders.Count == 0)
            {
                MessageBox.Show("Выберите хотя бы одну папку!", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Финальная валидация перед закрытием
            var validationResult = await _fileService.ValidateFoldersDetailedAsync(_selectedFolders);
            if (!validationResult.IsValid)
            {
                ErrorTextBlock.Text = validationResult.ErrorMessage ?? "Ошибка валидации папок.";
                MessageBox.Show(
                    $"Не удалось загрузить папки:\n\n{validationResult.ErrorMessage}\n\nПожалуйста, исправьте ошибки и попробуйте снова.",
                    "Ошибка валидации",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            DialogResult = true;
        }

        private async Task ValidateFoldersAsync()
        {
            ErrorTextBlock.Text = string.Empty;
            ErrorBorder.Visibility = Visibility.Collapsed;
            OkButton.IsEnabled = true;

            if (_selectedFolders.Count == 0)
            {
                // Сбрасываем ошибки для всех папок
                foreach (var folderInfo in _selectedFoldersInfo)
                {
                    folderInfo.HasError = false;
                    folderInfo.ErrorMessage = null;
                }
                return;
            }

            // Сначала сбрасываем все ошибки
            foreach (var folderInfo in _selectedFoldersInfo)
            {
                folderInfo.HasError = false;
                folderInfo.ErrorMessage = null;
            }

            var validationResult = await _fileService.ValidateFoldersDetailedAsync(_selectedFolders);

            if (!validationResult.IsValid)
            {
                ErrorTextBlock.Text = validationResult.ErrorMessage ?? "Ошибка валидации папок.";
                ErrorBorder.Visibility = Visibility.Visible;
                OkButton.IsEnabled = false;

                // Подсвечиваем проблемную папку
                if (!string.IsNullOrEmpty(validationResult.ProblemFolder))
                {
                    var problemFolder = _selectedFoldersInfo.FirstOrDefault(f => 
                        string.Equals(f.Path, validationResult.ProblemFolder, StringComparison.OrdinalIgnoreCase));
                    if (problemFolder != null)
                    {
                        problemFolder.HasError = true;
                        problemFolder.ErrorMessage = validationResult.ErrorMessage;
                    }
                }
            }
            else
            {
                ErrorTextBlock.Text = string.Empty;
                ErrorBorder.Visibility = Visibility.Collapsed;
                OkButton.IsEnabled = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}

