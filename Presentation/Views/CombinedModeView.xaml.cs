using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PhotoBookRenamer.Domain;
using PhotoBookRenamer.Presentation.ViewModels;

namespace PhotoBookRenamer.Presentation.Views
{
    public partial class CombinedModeView : UserControl
    {
        private string? _draggedFile;

        public CombinedModeView(CombinedModeViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void OnFileListMouseDown(object sender, MouseButtonEventArgs e)
        {
            // КРИТИЧЕСКИ ВАЖНО: Этот обработчик используется только как fallback
            // Основной обработчик - OnFileItemMouseDown, который вызывается напрямую из Border
            // Здесь ищем Border через визуальное дерево только если клик был не на Border
            
            if (e.OriginalSource is not FrameworkElement element)
                return;
            
            // Если клик был на Border - он уже обработан OnFileItemMouseDown
            if (element is Border border && border.DataContext is string filePath)
            {
                if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
                {
                    _draggedFile = filePath;
                    DragDrop.DoDragDrop(border, filePath, DragDropEffects.Copy);
                }
                return;
            }
            
            // Ищем Border, который является контейнером для файла в DataTemplate
            var parentBorder = FindParent<Border>(element);
            
            if (parentBorder != null && parentBorder.DataContext is string parentFilePath)
            {
                if (!string.IsNullOrEmpty(parentFilePath) && System.IO.File.Exists(parentFilePath))
                {
                    _draggedFile = parentFilePath;
                    DragDrop.DoDragDrop(parentBorder, parentFilePath, DragDropEffects.Copy);
                }
            }
        }

        private void OnFileListDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.Text))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void OnFileListDrop(object sender, DragEventArgs e)
        {
            // Handle drop on file list if needed
        }

        private void OnSlotDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.Text))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            
            // НЕ устанавливаем e.Handled = true для Preview событий, чтобы они могли пройти дальше
            if (!(e.RoutedEvent.Name?.StartsWith("Preview") ?? false))
            {
                e.Handled = true;
            }
        }

        private void OnCoverDrop(object sender, DragEventArgs e)
        {
            HandleDrop(sender, e, isCover: true);
        }

        private void OnPageDrop(object sender, DragEventArgs e)
        {
            HandleDrop(sender, e, isCover: false);
        }

        private void OnSlotClick(object sender, MouseButtonEventArgs e)
        {
            // КРИТИЧЕСКИ ВАЖНО: Открываем диалог выбора файла при клике на ПУСТУЮ ячейку
            if (sender is not Border border || border.DataContext is not Domain.Page page || DataContext is not CombinedModeViewModel vm)
            {
                return;
            }
            
            // Проверяем, что ячейка пустая
            if (!string.IsNullOrEmpty(page.SourcePath))
            {
                // Ячейка уже заполнена - не открываем диалог
                return;
            }
            
            // Открываем диалог выбора файла
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp|Все файлы|*.*",
                Title = "Выберите фото для загрузки"
            };
            
            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.FileName))
            {
                var filePath = dialog.FileName;
                vm.DropFileOnSlot(page, filePath, DropAction.ThisBookOnly);
            }
        }

        private void OnProjectNameClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not CombinedModeViewModel vm)
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
            // Проверяем, не потерял ли фокус из-за клика на кнопку или другой интерактивный элемент
            // Используем задержку, чтобы дать другим элементам обработать свои события
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                // Проверяем, не получил ли фокус какой-то другой элемент
                var focusedElement = Keyboard.FocusedElement;
                if (focusedElement is System.Windows.Controls.Button || focusedElement is System.Windows.Controls.TextBox)
                {
                    // Если фокус на кнопке или другом TextBox - не закрываем редактор сразу
                    // Дадим возможность обработать клик
                    return;
                }
                
                // Сохраняем изменения и скрываем TextBox
                SaveProjectName();
            }), System.Windows.Threading.DispatcherPriority.Input);
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
                if (DataContext is CombinedModeViewModel vm)
                {
                    ProjectNameTextBox.Text = vm.ProjectName ?? string.Empty;
                }
                ProjectNameTextBox.Visibility = Visibility.Collapsed;
                ProjectNameTextBlock.Visibility = Visibility.Visible;
                e.Handled = true;
            }
        }

        private void OnGridPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Если TextBox видим и клик был не на нем, закрываем редактор
            if (ProjectNameTextBox.Visibility == Visibility.Visible)
            {
                var source = e.OriginalSource as System.Windows.DependencyObject;
                if (source != null)
                {
                    // Проверяем, был ли клик на TextBox или его дочерних элементах
                    var textBox = FindParent<System.Windows.Controls.TextBox>(source);
                    if (textBox != ProjectNameTextBox)
                    {
                        // Проверяем, не был ли клик на кнопке
                        var button = FindParent<System.Windows.Controls.Button>(source);
                        
                        // Если клик был на кнопке - не закрываем редактор, дадим кнопке обработать клик
                        if (button == null)
                        {
                            // Клик был вне TextBox и не на кнопке - сохраняем изменения
                            // Используем задержку, чтобы не мешать другим обработчикам
                            e.Handled = false; // Не блокируем событие
                            Dispatcher.BeginInvoke(new System.Action(() => SaveProjectName()), System.Windows.Threading.DispatcherPriority.Loaded);
                        }
                    }
                }
            }
        }

        private T? FindParent<T>(System.Windows.DependencyObject child) where T : System.Windows.DependencyObject
        {
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T parentT)
                {
                    return parentT;
                }
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private void SaveProjectName()
        {
            if (DataContext is not CombinedModeViewModel vm)
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

        private async System.Threading.Tasks.Task SaveProjectNameAsync(CombinedModeViewModel vm)
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

        private void OnSlotDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.Text) || sender is not Border border || DataContext is not CombinedModeViewModel vm)
            {
                e.Handled = true;
                return;
            }
            
            var filePath = e.Data.GetData(DataFormats.Text) as string;
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            {
                e.Handled = true;
                return;
            }

            // КРИТИЧЕСКИ ВАЖНО: Теперь DataContext содержит Page напрямую, а не slotIndex
            if (border.DataContext is not Domain.Page page)
            {
                e.Handled = true;
                return;
            }

            if (page != null)
            {
                vm.DropFileOnSlot(page, filePath, DropAction.ThisBookOnly);
            }
            
            e.Handled = true;
        }

        private Book? FindBookParent(FrameworkElement element)
        {
            // Используем VisualTreeHelper для более надежного поиска
            // Структура: Border (slot) -> ItemsControl (AllSlots) -> StackPanel -> Border (book, DataContext=Book)
            
            var current = element as DependencyObject;
            int level = 0;
            
            while (current != null && level < 30)
            {
                // Проверяем, является ли текущий элемент Border с Book в DataContext
                if (current is Border border && border.DataContext is Book book)
                {
                    return book;
                }
                
                // Также проверяем ContentPresenter - в ItemsControl элементы оборачиваются в ContentPresenter
                if (current is ContentPresenter contentPresenter && contentPresenter.DataContext is Book book2)
                {
                    return book2;
                }
                
                // Проверяем ItemsControl - может содержать Book в DataContext
                if (current is System.Windows.Controls.ItemsControl itemsControl && itemsControl.DataContext is Book book3)
                {
                    return book3;
                }
                
                current = VisualTreeHelper.GetParent(current);
                level++;
            }
            
            return null;
        }

        private T? FindParent<T>(FrameworkElement element) where T : FrameworkElement
        {
            var parent = element.Parent as FrameworkElement;
            while (parent != null)
            {
                if (parent is T found)
                {
                    return found;
                }
                parent = parent.Parent as FrameworkElement;
            }
            return null;
        }

        private void HandleDrop(object sender, DragEventArgs e, bool isCover)
        {
            if (e.Data.GetDataPresent(DataFormats.Text))
            {
                var filePath = e.Data.GetData(DataFormats.Text) as string;
                if (string.IsNullOrEmpty(filePath)) return;

                var border = sender as Border;
                if (border == null || DataContext is not CombinedModeViewModel vm) return;

                Domain.Page? page = null;
                
                if (isCover)
                {
                    // Для обложки используем Tag
                    page = border.Tag as Domain.Page;
                }
                else
                {
                    // Для страницы используем Tag
                    page = border.Tag as Domain.Page;
                }

                if (page != null)
                {
                    // Показываем меню выбора действия
                    var menu = new ContextMenu();
                    
                    var item1 = new MenuItem { Header = "Оставить только для этой книги" };
                    item1.Click += (s, args) => 
                        vm.DropFileOnSlot(page, filePath, DropAction.ThisBookOnly);
                    
                    var item2 = new MenuItem { Header = "Применить ко всем книгам" };
                    item2.Click += (s, args) => 
                        vm.DropFileOnSlot(page, filePath, DropAction.AllBooks);
                    
                    var item3 = new MenuItem { Header = "Выбрать книги вручную" };
                    item3.Click += (s, args) => 
                    {
                        var dialog = new Presentation.Dialogs.BookSelectionDialog(vm.Books);
                        if (dialog.ShowDialog() == true && dialog.SelectedBooks.Count > 0)
                        {
                            vm.DropFileOnSlot(page, filePath, DropAction.SelectedBooks, dialog.SelectedBooks);
                        }
                    };
                    
                    menu.Items.Add(item1);
                    menu.Items.Add(item2);
                    menu.Items.Add(item3);
                    
                    menu.PlacementTarget = border;
                    menu.IsOpen = true;
                }
            }
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            var regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void OnImageDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement element && element.Tag is Domain.Page page)
            {
                OpenFileInViewer(page.SourcePath);
            }
        }

        private void OnFileImageDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement element && element.Tag is string filePath)
            {
                OpenFileInViewer(filePath);
            }
        }

        private void OpenFileInViewer(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
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

        private void OnPageImageDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement element)
            {
                if (element.Tag is Domain.Page page)
                {
                    OpenFileInViewer(page.SourcePath);
                }
            }
        }

        private void OnLoadFileClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && DataContext is CombinedModeViewModel vm)
            {
                // Получаем индекс слота из Tag кнопки
                if (button.Tag is int slotIndex)
                {
                    // Находим книгу через поиск родительского Border
                    var book = FindBookParent(button);
                    
                    if (book != null)
                    {
                        Domain.Page? page = null;
                        if (slotIndex == 0)
                        {
                            page = book.Cover;
                        }
                        else
                        {
                            // slotIndex - это номер разворота (1, 2, 3, 4...)
                            page = book.Pages.FirstOrDefault(p => p.Index == slotIndex && !p.IsCover);
                        }

                        if (page != null)
                        {
                            vm.LoadPageFileCommand.Execute(page);
                        }
                    }
                }
            }
        }

        private void OnDeletePageClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && DataContext is CombinedModeViewModel vm)
            {
                // Получаем индекс слота из Tag кнопки
                if (button.Tag is int slotIndex)
                {
                    // Находим книгу через поиск родительского Border
                    var book = FindBookParent(button);
                    if (book != null)
                    {
                        Domain.Page? page = null;
                        if (slotIndex == 0)
                        {
                            page = book.Cover;
                        }
                        else
                        {
                            page = book.Pages.FirstOrDefault(p => p.Index == slotIndex);
                        }

                        if (page != null)
                        {
                            vm.DeletePageCommand.Execute(page);
                        }
                    }
                }
            }
        }

        private void OnDeletePageFromAllBooksClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && DataContext is CombinedModeViewModel vm)
            {
                if (button.Tag is int slotIndex)
                {
                    var book = FindBookParent(button);
                    if (book != null)
                    {
                        if (slotIndex == 0)
                        {
                            // Обложка
                            if (book.Cover != null)
                            {
                                vm.DeletePageFromAllBooksCommand.Execute(book.Cover);
                            }
                        }
                        else
                        {
                            // Страница
                            var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
                            var pageIndex = slotIndex - 1;
                            if (pageIndex >= 0 && pageIndex < pagesWithoutCover.Count)
                            {
                                var page = pagesWithoutCover[pageIndex];
                                vm.DeletePageFromAllBooksCommand.Execute(page);
                            }
                        }
                    }
                }
            }
        }
        
        private void OnMovePageLeftClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && DataContext is CombinedModeViewModel vm)
            {
                if (button.Tag is int slotIndex && slotIndex > 0)
                {
                    var book = FindBookParent(button);
                    if (book != null)
                    {
                        var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
                        var pageIndex = slotIndex - 1;
                        if (pageIndex >= 0 && pageIndex < pagesWithoutCover.Count)
                        {
                            var page = pagesWithoutCover[pageIndex];
                            vm.MovePageLeftCommand.Execute(page);
                        }
                    }
                }
            }
        }
        
        private void OnMovePageRightClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && DataContext is CombinedModeViewModel vm)
            {
                if (button.Tag is int slotIndex && slotIndex > 0)
                {
                    var book = FindBookParent(button);
                    if (book != null)
                    {
                        var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
                        var pageIndex = slotIndex - 1;
                        if (pageIndex >= 0 && pageIndex < pagesWithoutCover.Count)
                        {
                            var page = pagesWithoutCover[pageIndex];
                            vm.MovePageRightCommand.Execute(page);
                        }
                    }
                }
            }
        }
        
        private void OnDuplicateToAllClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && DataContext is CombinedModeViewModel vm)
            {
                // Получаем индекс слота из Tag кнопки
                if (button.Tag is int slotIndex)
                {
                    // Находим книгу через поиск родительского Border
                    var book = FindBookParent(button);
                    if (book != null)
                    {
                        Domain.Page? page = null;
                        if (slotIndex == 0)
                        {
                            page = book.Cover;
                        }
                        else
                        {
                            page = book.Pages.FirstOrDefault(p => p.Index == slotIndex);
                        }

                        if (page != null)
                        {
                            vm.DuplicateToAllBooksCommand.Execute(page);
                        }
                    }
                }
            }
        }

        private void OnUploadAreaLoaded(object sender, RoutedEventArgs e)
        {
            // Метод оставлен для совместимости, но логирование удалено для производительности
        }
        
        private void OnUploadAreaClick(object sender, MouseButtonEventArgs e)
        {
            // Вызываем команду загрузки файлов
            if (DataContext is CombinedModeViewModel vm)
            {
                vm.LoadFilesCommand?.Execute(null);
            }
        }

        private void OnFileItemMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border border || border.DataContext is not string filePath)
                return;
            
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                return;
            
            _draggedFile = filePath;
            DragDrop.DoDragDrop(border, filePath, DragDropEffects.Copy);
        }

        private T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            do
            {
                if (current is T ancestor)
                {
                    return ancestor;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            while (current != null);
            return null;
        }

        // Логи для отладки размера ячеек и фото
        private void OnCellLoaded(object sender, RoutedEventArgs e)
        {
        }

        private void OnPhotoGridLoaded(object sender, RoutedEventArgs e)
        {
        }

        private void OnImageLoaded(object sender, RoutedEventArgs e)
        {
            // UniformToFill сам растягивает изображение по ширине контейнера
            // ScaleTransform с ScaleY="0.97" применяется для уменьшения высоты на 3%
        }

        private void OnImageSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // UniformToFill сам обрабатывает изменение размера
        }

        private void OnImageLayoutUpdated(object sender, EventArgs e)
        {
            // UniformToFill сам обрабатывает обновление layout
        }

        private void ScaleImageToWidth(Image image, double targetWidth)
        {
            if (image.Source == null || image.Source.Width == 0 || image.ActualWidth == 0)
            {
                return;
            }

            double currentWidth = image.ActualWidth;
            if (currentWidth == 0) return; // Избегаем деления на ноль

            double scale = targetWidth / currentWidth;

            ScaleTransform scaleTransform;
            if (image.LayoutTransform is ScaleTransform existingScaleTransform)
            {
                scaleTransform = existingScaleTransform.IsFrozen ? existingScaleTransform.Clone() : existingScaleTransform;
            }
            else
            {
                scaleTransform = new ScaleTransform();
            }

            var scaleX = scale;
            var scaleY = scale * 0.97; // Уменьшаем высоту на 3%

            scaleTransform.ScaleX = scaleX;
            scaleTransform.ScaleY = scaleY;
            image.LayoutTransform = scaleTransform;
            image.InvalidateMeasure(); // Принудительно пересчитываем размеры
        }

    }
}

