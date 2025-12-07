using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhotoBookRenamer.Models;
using PhotoBookRenamer.ViewModels;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PhotoBookRenamer.Views
{
    public partial class UniqueFoldersView : UserControl
    {

        public UniqueFoldersView(UniqueFoldersViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void OnCoverImageDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement element && element.Tag is Models.Page page)
            {
                OpenFileInViewer(page.SourcePath);
            }
        }

        private void OnPageImageDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is Image image)
            {
                // Получаем ItemsControl и книгу по позиции
                var itemsControl = FindAncestor<ItemsControl>(image);
                if (itemsControl?.DataContext is Models.Book book)
                {
                    // Находим индекс из DataContext Border
                    var border = FindAncestor<Border>(image);
                    if (border?.DataContext is int index)
                    {
                        if (index == 0)
                        {
                            // Обложка
                            if (book.Cover != null)
                            {
                                OpenFileInViewer(book.Cover.SourcePath);
                            }
                        }
                        else
                        {
                            // Страница
                            var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
                            var pageIndex = index - 1;
                            
                            if (pageIndex >= 0 && pageIndex < pagesWithoutCover.Count)
                            {
                                var page = pagesWithoutCover[pageIndex];
                                OpenFileInViewer(page.SourcePath);
                            }
                        }
                    }
                }
            }
        }

        private static T? FindAncestor<T>(System.Windows.DependencyObject current) where T : System.Windows.DependencyObject
        {
            while (current != null)
            {
                if (current is T ancestor)
                    return ancestor;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void OpenFileInViewer(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть файл: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnMovePageLeftClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && DataContext is UniqueFoldersViewModel viewModel)
            {
                // Получаем индекс из DataContext Border (это int: 0 = обложка, 1+ = страницы)
                var border = FindAncestor<Border>(button);
                if (border?.DataContext is int index && index > 0) // Игнорируем обложку
                {
                    // Получаем Book из DataContext ItemsControl
                    var itemsControl = FindAncestor<ItemsControl>(button);
                    if (itemsControl?.DataContext is Models.Book book)
                    {
                        var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
                        var pageIndex = index - 1; // index в AllSlots: 1 = первая страница (pageIndex 0), 2 = вторая (pageIndex 1) и т.д.
                        
                        if (pageIndex > 0 && pageIndex < pagesWithoutCover.Count)
                        {
                            var page = pagesWithoutCover[pageIndex];
                            // Выполняем команду перемещения - AllSlots обновится автоматически
                            viewModel.MovePageUpCommand.Execute(page);
                        }
                    }
                }
            }
        }

        private void OnMovePageRightClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && DataContext is UniqueFoldersViewModel viewModel)
            {
                // Получаем индекс из DataContext Border (это int: 0 = обложка, 1+ = страницы)
                var border = FindAncestor<Border>(button);
                if (border?.DataContext is int index && index > 0) // Игнорируем обложку
                {
                    // Получаем Book из DataContext ItemsControl
                    var itemsControl = FindAncestor<ItemsControl>(button);
                    if (itemsControl?.DataContext is Models.Book book)
                    {
                        var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
                        var pageIndex = index - 1; // index в AllSlots начинается с 1 для страниц
                        
                        if (pageIndex >= 0 && pageIndex < pagesWithoutCover.Count - 1)
                        {
                            var page = pagesWithoutCover[pageIndex];
                            // Выполняем команду перемещения - AllSlots обновится автоматически
                            viewModel.MovePageDownCommand.Execute(page);
                        }
                    }
                }
            }
        }

        private void OnAssignPageNumberClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && DataContext is UniqueFoldersViewModel viewModel)
            {
                // Получаем индекс из DataContext Border (это int: 0 = обложка, 1+ = страницы)
                var border = FindAncestor<Border>(button);
                if (border?.DataContext is int index && index > 0) // Игнорируем обложку
                {
                    // Получаем Book из DataContext ItemsControl
                    var itemsControl = FindAncestor<ItemsControl>(button);
                    if (itemsControl?.DataContext is Models.Book book)
                    {
                        var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
                        var pageIndex = index - 1; // index в AllSlots начинается с 1 для страниц
                        
                        if (pageIndex >= 0 && pageIndex < pagesWithoutCover.Count)
                        {
                            var page = pagesWithoutCover[pageIndex];
                            // Выполняем команду назначения номера - AllSlots обновится автоматически
                            viewModel.AssignPageNumberCommand.Execute(page);
                        }
                    }
                }
            }
        }

        private void RefreshItemsControl(ItemsControl itemsControl)
        {
            if (itemsControl == null) return;
            
            // Обновляем ItemsSource для принудительного обновления всех привязок
            var itemsSource = itemsControl.ItemsSource;
            
            // Сначала обновляем привязку к Pages в конвертере PageSlotConverter
            // Это заставит пересоздать слоты страниц
            itemsControl.ItemsSource = null;
            
            // Используем Dispatcher для обновления в следующем кадре с высоким приоритетом
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                new System.Action(() =>
                {
                    itemsControl.ItemsSource = itemsSource;
                    
                    // Принудительно обновляем layout
                    itemsControl.UpdateLayout();
                    
                    // Обновляем привязки в дочерних элементах
                    UpdateBindingsRecursive(itemsControl);
                }));
        }

        private void UpdateBindingsRecursive(System.Windows.DependencyObject obj)
        {
            if (obj == null) return;
            
            // Обновляем привязки для Image.Source (используется PageByIndexConverter)
            if (obj is System.Windows.Controls.Image image)
            {
                var binding = System.Windows.Data.BindingOperations.GetBindingExpression(image, System.Windows.Controls.Image.SourceProperty);
                binding?.UpdateTarget();
            }
            
            // Обновляем привязки для TextBlock.Text (используется PageFileNameConverter)
            if (obj is System.Windows.Controls.TextBlock textBlock)
            {
                var binding = System.Windows.Data.BindingOperations.GetBindingExpression(textBlock, System.Windows.Controls.TextBlock.TextProperty);
                binding?.UpdateTarget();
            }
            
            // Рекурсивно обновляем дочерние элементы
            var childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(obj);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(obj, i);
                UpdateBindingsRecursive(child);
            }
        }

        // Логи для отладки размера ячеек и фото
        private void OnCellLoaded(object sender, RoutedEventArgs e)
        {
        }

        private void OnImageLoaded(object sender, RoutedEventArgs e)
        {
            // Stretch="Fill" сам растягивает изображение по ширине и высоте контейнера
        }

        private void OnImageLayoutUpdated(object sender, EventArgs e)
        {
            // Stretch="Fill" сам обрабатывает обновление layout
        }

        private void OnImageSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Stretch="Fill" сам обрабатывает изменение размера
        }

        private void OnPhotoGridLoaded(object sender, RoutedEventArgs e)
        {
        }

        private void OnProjectNameClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not UniqueFoldersViewModel vm)
            {
                return;
            }

            // Устанавливаем текущее значение в TextBox
            ProjectNameTextBox.Text = vm.ProjectName ?? string.Empty;
            
            // Показываем TextBox для редактирования
            ProjectNameTextBlock.Visibility = Visibility.Collapsed;
            ProjectNameTextBox.Visibility = Visibility.Visible;
            ProjectNameTextBox.Focus();
            ProjectNameTextBox.SelectAll();
        }

        private void OnProjectNameLostFocus(object sender, RoutedEventArgs e)
        {
            // Используем Dispatcher.BeginInvoke, чтобы избежать немедленного закрытия редактора
            // когда фокус переходит на кнопку
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Проверяем, что TextBox все еще видим (не был закрыт по Escape)
                if (ProjectNameTextBox.Visibility == Visibility.Visible)
                {
                    SaveProjectName();
                }
            }), DispatcherPriority.Input);
        }

        private void OnProjectNameKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Сохраняем изменения при нажатии Enter
                SaveProjectName();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // Отменяем редактирование при нажатии Escape
                if (DataContext is UniqueFoldersViewModel vm)
                {
                    ProjectNameTextBox.Text = vm.ProjectName ?? string.Empty;
                }
                ProjectNameTextBox.Visibility = Visibility.Collapsed;
                ProjectNameTextBlock.Visibility = Visibility.Visible;
                e.Handled = true;
            }
        }

        private void SaveProjectName()
        {
            if (DataContext is not UniqueFoldersViewModel vm)
            {
                // Если нет ViewModel, просто закрываем редактор
                ProjectNameTextBox.Visibility = Visibility.Collapsed;
                ProjectNameTextBlock.Visibility = Visibility.Visible;
                return;
            }

            // Обновляем название проекта в ViewModel
            var newName = ProjectNameTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(newName))
            {
                // Если название пустое, возвращаем старое значение
                ProjectNameTextBox.Text = vm.ProjectName;
            }
            else if (newName != vm.ProjectName)
            {
                // Обновляем название только если оно изменилось
                vm.ProjectName = newName;
                
                // Обновляем CurrentProjectInfo
                if (vm.CurrentProjectInfo != null)
                {
                    vm.CurrentProjectInfo.Name = newName;
                    vm.CurrentProjectInfo.LastModified = DateTime.Now;
                    
                    // Сохраняем изменения в файл асинхронно, но не блокируем UI
                    _ = SaveProjectNameAsync(vm);
                }
            }

            // Скрываем TextBox и показываем TextBlock сразу (не ждем сохранения)
            ProjectNameTextBox.Visibility = Visibility.Collapsed;
            ProjectNameTextBlock.Visibility = Visibility.Visible;
        }

        private async System.Threading.Tasks.Task SaveProjectNameAsync(UniqueFoldersViewModel vm)
        {
            try
            {
                // Сохраняем только название проекта без полного сохранения и без переключения режима
                await vm.SaveProjectNameOnlyAsync();
            }
            catch (Exception ex)
            {
                // Ошибка сохранения названия проекта
            }
        }

    }
}





