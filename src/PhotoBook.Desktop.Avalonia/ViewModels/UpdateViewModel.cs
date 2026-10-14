using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoBook.Application;

namespace PhotoBook.Shell.ViewModels
{
    /// <summary>
    /// What the update window shows, and what happens when someone presses "Обновить".
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is the shell's own and not the shared layer's, because two of its jobs are the
    /// shell's: showing a message without a system box, and ending the session once the
    /// installer is running. The other shell's version does those two things with the desktop
    /// framework directly, which is one of the reasons it lives in that shell.
    /// </para>
    /// <para>
    /// The notes are handed out as plain classified lines rather than a formatted document.
    /// That document type belongs to one engine, and putting it in the shared layer would
    /// bind every shell to it - which is the mistake that put a document type in the layer
    /// below in the first place.
    /// </para>
    /// </remarks>
    public sealed partial class UpdateViewModel : ObservableObject
    {
        private readonly IUpdateFeed _updates;

        private string _currentVersionText = string.Empty;
        private string _latestVersionText = string.Empty;
        private IReadOnlyList<ReleaseNoteLine> _notes = Array.Empty<ReleaseNoteLine>();
        private bool _isDownloading;
        private double _downloadProgress;
        private string _downloadProgressText = string.Empty;
        private bool _canUpdate = true;

        public UpdateViewModel(IUpdateFeed updates, string latestVersion, string? releaseNotes)
        {
            _updates = updates;

            CurrentVersionText = $"Текущая версия: {updates.GetCurrentVersion()}";
            LatestVersionText = $"Новая версия: {latestVersion}";
            Notes = ReleaseNotesText.Parse(releaseNotes);

            UpdateCommand = new AsyncRelayCommand(UpdateAsync, () => CanUpdate && !IsDownloading);
            PostponeCommand = new RelayCommand(() => PostponeRequested?.Invoke(this, EventArgs.Empty));
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

        /// <summary>The release text, already stripped of what does not face a person.</summary>
        public IReadOnlyList<ReleaseNoteLine> Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value);
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set
            {
                if (!SetProperty(ref _isDownloading, value)) return;
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
                if (!SetProperty(ref _canUpdate, value)) return;
                UpdateCommand.NotifyCanExecuteChanged();
            }
        }

        public AsyncRelayCommand UpdateCommand { get; }

        public RelayCommand PostponeCommand { get; }

        /// <summary>Raised when the installer is running and the session should end.</summary>
        public event EventHandler? InstallerStarted;

        /// <summary>Raised when someone presses "later".</summary>
        public event EventHandler? PostponeRequested;

        /// <summary>Something went wrong, in words a person can act on.</summary>
        public event EventHandler<string>? Failed;

        private async Task UpdateAsync()
        {
            IsDownloading = true;
            CanUpdate = false;
            DownloadProgress = 0;
            DownloadProgressText = "Подготовка...";

            try
            {
                var downloadUrl = await _updates.GetDownloadUrlAsync();
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    Fail("Не удалось получить ссылку для загрузки обновления.");
                    return;
                }

                var progress = new Progress<double>(percent =>
                {
                    DownloadProgress = percent;
                    DownloadProgressText = $"Загружено: {percent:F1}%";
                });

                var installer = await _updates.DownloadInstallerAsync(downloadUrl, progress);

                if (installer is null)
                {
                    Fail("Не удалось загрузить или установить обновление.");
                    return;
                }

                // The installer is running and will replace the files of this installation.
                // Ending the session is the shell's decision, not the feed's: the feed knows
                // where the update came from and nothing about this window.
                await Task.Delay(1000);
                InstallerStarted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Fail($"Ошибка при обновлении: {ex.Message}");
            }
        }

        private void Fail(string message)
        {
            Failed?.Invoke(this, message);
            IsDownloading = false;
            CanUpdate = true;
        }
    }
}
