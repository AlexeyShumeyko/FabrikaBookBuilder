using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PhotoBookRenamer.Application;
using PhotoBookRenamer.Infrastructure;
using PhotoBookRenamer.Presentation.ViewModels;
using PhotoBookRenamer.Presentation.Views;

namespace PhotoBookRenamer
{
    public partial class App : System.Windows.Application
    {
        private ServiceProvider? _serviceProvider;

        // There is deliberately no accessor for the container. Every screen and view
        // model used to reach the shell through one, which is how a lookup ended up
        // inside a click handler: it works until it does not, and it fails by doing
        // nothing. Dependencies now arrive through constructors and the shell is
        // asked for what it knows. If a new caller needs the container, that is the
        // question to answer here rather than in the caller.

        /// <summary>
        /// Dumps WPF data-binding diagnostics to %TEMP%\PhotoBookRenamer\binding.log when
        /// the FBR_BINDING_TRACE environment variable is set.
        ///
        /// A silently broken binding in WPF does not throw: the target keeps its default
        /// value, so a failed converter or a typo in a property path looks like "the
        /// feature just does not work". This makes those failures visible.
        /// </summary>
        private void EnableBindingTraceIfRequested()
        {
            var mode = Environment.GetEnvironmentVariable("FBR_BINDING_TRACE");
            if (string.IsNullOrWhiteSpace(mode)) return;

            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "PhotoBookRenamer");
                Directory.CreateDirectory(dir);
                var log = Path.Combine(dir, "binding.log");
                if (File.Exists(log)) File.Delete(log);

                var listener = new TextWriterTraceListener(new StreamWriter(log, append: false))
                {
                    TraceOutputOptions = TraceOptions.Timestamp | TraceOptions.Callstack
                };

                var level = string.Equals(mode, "verbose", StringComparison.OrdinalIgnoreCase)
                    ? SourceLevels.Warning | SourceLevels.Information
                    : SourceLevels.Warning;

                PresentationTraceSources.Refresh();
                PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = level;
                PresentationTraceSources.DataBindingSource.Listeners.Add(
                    new ConsoleTraceListener());

                Debug.WriteLine($"[FBR] binding trace -> {log}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FBR] binding trace could not be enabled: {ex.Message}");
            }
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            EnableBindingTraceIfRequested();

            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            // Проверка обновлений в фоне
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000); // Ждем 2 секунды после запуска приложения

                    var updateService = _serviceProvider.GetRequiredService<IUpdateFeed>();
                    var hasUpdate = await updateService.CheckForUpdatesAsync();
                    if (hasUpdate)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(async () =>
                        {
                            var latestVersion = await updateService.GetLatestVersionAsync();
                            var releaseNotes = await updateService.GetLatestReleaseNotesAsync();

                            if (!string.IsNullOrEmpty(latestVersion))
                            {
                                var updateVm = new Presentation.ViewModels.UpdateDialogViewModel(updateService, latestVersion, releaseNotes);
                                var updateDialog = new Presentation.Views.UpdateDialog(updateVm);
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

            // The two layers below assemble themselves: each knows what implements its
            // ports, and the implementations are internal, so this is the only place in
            // the program that could name a concrete one - and it no longer does.
            services.AddPhotoBookApplication();
            services.AddPhotoBookInfrastructure();

            // The shell's own adapter: choosing a file needs a window, so it lives here.
            services.AddSingleton<IPickFiles, Presentation.Services.WpfFilePicker>();

            // ViewModels
            // MainViewModel MUST be a singleton: it owns the persistent top bar, which has
            // to survive every screen change. As a transient it was re-created on every
            // navigation, which tore the header down each time. The editors are singletons
            // for the same reason - their editing state has to outlive a view swap.
            services.AddSingleton<MainViewModel>();
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

