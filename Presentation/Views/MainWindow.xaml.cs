using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using PhotoBookRenamer.Domain;
using PhotoBookRenamer.Application;
using PhotoBookRenamer.Presentation.ViewModels;

namespace PhotoBookRenamer.Presentation.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;
            
            // Apply Windows 11 Fluent Design
            ApplyFluentDesign();
            
            // Настройка горячих клавиш
            SetupKeyboardShortcuts();
            
            // Настройка размеров окна
            SetupWindowSize();
            
            // Представление обновляется автоматически через свойство CurrentMode
            UpdateView();
        }

        private void SetupWindowSize()
        {
            // Получаем размеры рабочей области экрана
            var screenWidth = SystemParameters.WorkArea.Width;
            var screenHeight = SystemParameters.WorkArea.Height;
            
            // Устанавливаем разумные размеры по умолчанию
            Width = 1400;
            Height = 900;
            
            // Ограничиваем размеры, если они больше экрана
            if (Width > screenWidth * 0.9)
            {
                Width = screenWidth * 0.9;
            }
            if (Height > screenHeight * 0.9)
            {
                Height = screenHeight * 0.9;
            }
            
            // Минимальные размеры
            MinWidth = 1000;
            MinHeight = 600;
            
            // Убираем ограничения максимальных размеров для правильной работы в развёрнутом виде
            // MaxWidth и MaxHeight не устанавливаем, чтобы окно могло правильно максимизироваться
            
            // Центрируем окно
            Left = (screenWidth - Width) / 2;
            Top = (screenHeight - Height) / 2;
            
            // Подписываемся на изменение состояния окна
            StateChanged += MainWindow_StateChanged;
            SizeChanged += MainWindow_SizeChanged;
        }
        
        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            // При максимизации убеждаемся, что все элементы правильно растягиваются
            if (WindowState == WindowState.Maximized)
            {
                // Принудительно обновляем layout
                InvalidateVisual();
                UpdateLayout();
            }
        }
        
        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // При изменении размера окна принудительно обновляем layout
            InvalidateVisual();
            UpdateLayout();
        }

        private void Border_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Убрано - теперь используется стандартная панель Windows
        }

        private void ApplyFluentDesign()
        {
            // Set window background to Fluent color
            Background = new SolidColorBrush(Color.FromRgb(243, 243, 243));
        }

        private void SetupKeyboardShortcuts()
        {
            // Ctrl+1 - Уникальные папки
            InputBindings.Add(new KeyBinding(_viewModel.SwitchToUniqueFoldersCommand, 
                Key.D1, ModifierKeys.Control));
            
            // Ctrl+2 - Комбинированный режим
            InputBindings.Add(new KeyBinding(_viewModel.SwitchToCombinedModeCommand, 
                Key.D2, ModifierKeys.Control));

            // Обработка горячих клавиш для текущего режима
            KeyDown += MainWindow_KeyDown;
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            var serviceProvider = ((App)System.Windows.Application.Current).GetServiceProvider();
            if (serviceProvider == null) return;

            // Ctrl+O - открыть файлы (комбинированный режим)
            if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_viewModel.CurrentMode == AppMode.Combined)
                {
                    var combinedVm = serviceProvider.GetService<CombinedModeViewModel>();
                    combinedVm?.LoadFilesCommand.Execute(null);
                    e.Handled = true;
                }
            }
            // Ctrl+Shift+O - открыть папки (уникальные папки)
            else if (e.Key == Key.O && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                if (_viewModel.CurrentMode == AppMode.UniqueFolders)
                {
                    var uniqueVm = serviceProvider.GetService<UniqueFoldersViewModel>();
                    uniqueVm?.LoadFoldersCommand.Execute(null);
                    e.Handled = true;
                }
            }
            // Ctrl+S - экспорт
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_viewModel.CurrentMode == AppMode.UniqueFolders)
                {
                    var uniqueVm = serviceProvider.GetService<UniqueFoldersViewModel>();
                    uniqueVm?.ExportCommand.Execute(null);
                    e.Handled = true;
                }
                else if (_viewModel.CurrentMode == AppMode.Combined)
                {
                    var combinedVm = serviceProvider.GetService<CombinedModeViewModel>();
                    combinedVm?.ExportCommand.Execute(null);
                    e.Handled = true;
                }
            }
            // Ctrl+Shift+S - экспорт с выбором папки
            else if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                if (_viewModel.CurrentMode == AppMode.UniqueFolders)
                {
                    var uniqueVm = serviceProvider.GetService<UniqueFoldersViewModel>();
                    uniqueVm?.ExportWithFolderCommand.Execute(null);
                    e.Handled = true;
                }
                else if (_viewModel.CurrentMode == AppMode.Combined)
                {
                    var combinedVm = serviceProvider.GetService<CombinedModeViewModel>();
                    combinedVm?.ExportWithFolderCommand.Execute(null);
                    e.Handled = true;
                }
            }
            // Ctrl+E - пересоздать проект
            else if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_viewModel.CurrentMode == AppMode.UniqueFolders)
                {
                    var uniqueVm = serviceProvider.GetService<UniqueFoldersViewModel>();
                    uniqueVm?.ResetProjectCommand.Execute(null);
                    e.Handled = true;
                }
                else if (_viewModel.CurrentMode == AppMode.Combined)
                {
                    var combinedVm = serviceProvider.GetService<CombinedModeViewModel>();
                    combinedVm?.ResetProjectCommand.Execute(null);
                    e.Handled = true;
                }
            }
            // Ctrl+Z - отменить
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_viewModel.CurrentMode == AppMode.UniqueFolders)
                {
                    var uniqueVm = serviceProvider.GetService<UniqueFoldersViewModel>();
                    uniqueVm?.UndoCommand.Execute(null);
                    e.Handled = true;
                }
            }
            // Ctrl+Y - повторить
            else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_viewModel.CurrentMode == AppMode.UniqueFolders)
                {
                    var uniqueVm = serviceProvider.GetService<UniqueFoldersViewModel>();
                    uniqueVm?.RedoCommand.Execute(null);
                    e.Handled = true;
                }
            }
            // Del - удалить элемент
            else if (e.Key == Key.Delete)
            {
                // Обработка удаления будет в соответствующих ViewModels
                e.Handled = false; // Позволяем обработать в дочерних элементах
            }
        }

        private void UpdateView()
        {
            var serviceProvider = ((App)System.Windows.Application.Current).GetServiceProvider();
            if (serviceProvider == null) return;

            switch (_viewModel.CurrentMode)
            {
                case AppMode.StartScreen:
                    _viewModel.CurrentView = new StartScreenView(_viewModel);
                    break;
                case AppMode.ProjectList:
                    var projectListService = serviceProvider.GetRequiredService<IProjectListService>();
                    var projectListVm = new ProjectListViewModel(projectListService);
                    _viewModel.CurrentView = new ProjectListView(projectListVm);
                    break;
                case AppMode.UniqueFolders:
                    var uniqueVm = serviceProvider.GetRequiredService<UniqueFoldersViewModel>();
                    // Создаем новый View, но используем существующий ViewModel (Singleton), который сохраняет состояние
                    _viewModel.CurrentView = new UniqueFoldersView(uniqueVm);
                    break;
                case AppMode.Combined:
                    var combinedVm = serviceProvider.GetRequiredService<CombinedModeViewModel>();
                    // Создаем новый View, но используем существующий ViewModel (Singleton), который сохраняет состояние
                    _viewModel.CurrentView = new CombinedModeView(combinedVm);
                    break;
                case AppMode.Help:
                    var helpSection = _viewModel.HelpSection ?? HelpSection.Overview;
                    var helpVm = new HelpViewModel(helpSection);
                    _viewModel.CurrentView = new HelpView(helpVm);
                    break;
            }
        }

    }
}
