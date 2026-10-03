using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoBookRenamer.Domain;
using PhotoBookRenamer.Application;
using PhotoBookRenamer.Infrastructure;

namespace PhotoBookRenamer.Presentation.ViewModels
{
    public class UpdateDialogViewModel : ViewModelBase
    {
        private readonly IUpdateService _updateService;
        private string _currentVersionText = "";
        private string _latestVersionText = "";
        private System.Windows.Documents.FlowDocument _notes =
            ReleaseNotesFormatter.Build(null);
        private bool _isDownloading;
        private double _downloadProgress;
        private string _downloadProgressText = "";
        private bool _canUpdate = true;

        public UpdateDialogViewModel(IUpdateService updateService, string latestVersion, string? releaseNotes)
        {
            _updateService = updateService;
            CurrentVersionText = $"Текущая версия: {_updateService.GetCurrentVersion()}";
            LatestVersionText = $"Новая версия: {latestVersion}";
            // Rendered once, here: the dialog binds a document, not the raw markdown.
            // ReleaseNotesFormatter also drops what does not belong in front of a user -
            // links and GitHub's generated block with the author's account.
            Notes = ReleaseNotesFormatter.Build(releaseNotes);
            
            UpdateCommand = new AsyncRelayCommand(UpdateAsync, () => CanUpdate && !IsDownloading);
            PostponeCommand = new RelayCommand(Postpone);
        }

        public string CurrentVersionText
        {
            get => _currentVersionText;
            set => SetProperty(ref _currentVersionText, value);
        }

        public string LatestVersionText
        {
            get => _latestVersionText;
            set => SetProperty(ref _latestVersionText, value);
        }

        /// <summary>The release text, formatted for reading. Set once, at construction.</summary>
        public System.Windows.Documents.FlowDocument Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value);
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set
            {
                SetProperty(ref _isDownloading, value);
                UpdateCommand.NotifyCanExecuteChanged();
            }
        }

        public double DownloadProgress
        {
            get => _downloadProgress;
            set => SetProperty(ref _downloadProgress, value);
        }

        public string DownloadProgressText
        {
            get => _downloadProgressText;
            set => SetProperty(ref _downloadProgressText, value);
        }

        public bool CanUpdate
        {
            get => _canUpdate;
            set
            {
                SetProperty(ref _canUpdate, value);
                UpdateCommand.NotifyCanExecuteChanged();
            }
        }

        public AsyncRelayCommand UpdateCommand { get; }
        public RelayCommand PostponeCommand { get; }

        private async Task UpdateAsync()
        {
            try
            {
                IsDownloading = true;
                CanUpdate = false;
                DownloadProgress = 0;
                DownloadProgressText = "Подготовка...";

                var downloadUrl = await _updateService.GetDownloadUrlAsync();
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    MessageBox.Show("Не удалось получить ссылку для загрузки обновления.", 
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    IsDownloading = false;
                    CanUpdate = true;
                    return;
                }

                var progress = new Progress<double>(percent =>
                {
                    DownloadProgress = percent;
                    DownloadProgressText = $"Загружено: {percent:F1}%";
                });

                var success = await _updateService.DownloadAndInstallUpdateAsync(downloadUrl, progress);
                
                if (!success)
                {
                    MessageBox.Show("Не удалось загрузить или установить обновление.", 
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    IsDownloading = false;
                    CanUpdate = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при обновлении: {ex.Message}", 
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                IsDownloading = false;
                CanUpdate = true;
            }
        }

        private void Postpone()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var window = System.Windows.Application.Current.Windows.OfType<Presentation.Views.UpdateDialog>().FirstOrDefault();
                window?.Close();
            });
        }
    }
}

