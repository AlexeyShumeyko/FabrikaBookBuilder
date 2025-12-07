using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PhotoBookRenamer.Services;
using PhotoBookRenamer.ViewModels;
using PhotoBookRenamer.Views;

namespace PhotoBookRenamer
{
    public partial class App : Application
    {
        private ServiceProvider? _serviceProvider;

        public IServiceProvider? GetServiceProvider() => _serviceProvider;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            // Проверка обновлений в фоне
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000); // Ждем 2 секунды после запуска приложения
                    
                    var updateService = _serviceProvider.GetRequiredService<IUpdateService>();
                    var hasUpdate = await updateService.CheckForUpdatesAsync();
                    if (hasUpdate)
                    {
                        Application.Current.Dispatcher.Invoke(async () =>
                        {
                            var latestVersion = await updateService.GetLatestVersionAsync();
                            var releaseNotes = await updateService.GetLatestReleaseNotesAsync();
                            
                            if (!string.IsNullOrEmpty(latestVersion))
                            {
                                var updateVm = new ViewModels.UpdateDialogViewModel(updateService, latestVersion, releaseNotes);
                                var updateDialog = new Views.UpdateDialog(updateVm);
                                updateDialog.ShowDialog();
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    // Логируем ошибки, но не показываем пользователю
                    System.Diagnostics.Debug.WriteLine($"Ошибка проверки обновлений: {ex.Message}");
                }
            });

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Services
            services.AddSingleton<IFileService, FileService>();
            services.AddSingleton<IImageService, ImageService>();
            services.AddSingleton<IExportService, ExportService>();
            services.AddSingleton<IUpdateService, UpdateService>();
            services.AddSingleton<ILoggingService, LoggingService>();
            services.AddSingleton<IProjectService, ProjectService>();
            services.AddSingleton<IProjectListService, ProjectListService>();

            // ViewModels
            services.AddTransient<MainViewModel>();
            services.AddSingleton<UniqueFoldersViewModel>();
            services.AddSingleton<CombinedModeViewModel>();

            // Views
            services.AddTransient<MainWindow>();
            services.AddTransient<UniqueFoldersView>();
            services.AddTransient<CombinedModeView>();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _serviceProvider?.Dispose();
            base.OnExit(e);
        }
    }
}

