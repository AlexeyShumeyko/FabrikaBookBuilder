using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using PhotoBookRenamer.Models;
using PhotoBookRenamer.Services;
using PhotoBookRenamer.ViewModels;

namespace PhotoBookRenamer.Views
{
    public partial class StartScreenView : UserControl
    {
        private readonly MainViewModel _viewModel;

        public StartScreenView(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
        }

        private async void OnUniqueFoldersClick(object sender, RoutedEventArgs e)
        {
            await CreateAndOpenProjectAsync(AppMode.UniqueFolders);
        }

        private async void OnCombinedModeClick(object sender, RoutedEventArgs e)
        {
            await CreateAndOpenProjectAsync(AppMode.Combined);
        }

        private void OnBackClick(object sender, RoutedEventArgs e)
        {
            _viewModel.CurrentMode = AppMode.ProjectList;
        }

        private void OnHelpClick(object sender, RoutedEventArgs e)
        {
            _viewModel.OpenHelp(HelpSection.StartScreen);
        }

        private async System.Threading.Tasks.Task CreateAndOpenProjectAsync(AppMode mode)
        {
            try
            {
                var serviceProvider = ((App)Application.Current).GetServiceProvider();
                if (serviceProvider == null) return;

                var projectListService = serviceProvider.GetRequiredService<IProjectListService>();
                
                // Создаем новый проект
                var projectName = $"Новый проект {DateTime.Now:yyyy-MM-dd HH:mm}";
                var projectInfo = await projectListService.CreateProjectAsync(mode, projectName);
                
                if (projectInfo != null)
                {
                    // Переключаемся на режим редактирования
                    _viewModel.CurrentMode = mode;
                    
                    // Устанавливаем проект в ViewModel синхронно
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (mode == AppMode.UniqueFolders)
                        {
                            var uniqueVm = serviceProvider.GetRequiredService<UniqueFoldersViewModel>();
                            var project = new Project { Mode = mode };
                            uniqueVm.SetProject(project, projectInfo);
                        }
                        else if (mode == AppMode.Combined)
                        {
                            var combinedVm = serviceProvider.GetRequiredService<CombinedModeViewModel>();
                            var project = new Project { Mode = mode };
                            combinedVm.SetProject(project, projectInfo);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка создания проекта: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

