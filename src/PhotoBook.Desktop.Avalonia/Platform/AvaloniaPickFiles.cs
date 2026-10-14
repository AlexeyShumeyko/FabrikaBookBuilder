using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PhotoBook.Application;

namespace PhotoBook.Shell.Platform
{
    /// <summary>
    /// Choosing files, through this shell's own file dialog.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Avalonia reaches the platform's picker through the storage provider on a window,
    /// rather than through a static call the way WPF does. So this is an instance, and the
    /// window is something it is given rather than something it looks up - which is also why
    /// it can be built before there is a window.
    /// </para>
    /// <para>
    /// Only JPEG is offered, matching what the program can actually read. A picker that let
    /// someone choose a format the exporter cannot handle would only move the failure later,
    /// to a screen with no way back.
    /// </para>
    /// </remarks>
    public class AvaloniaPickFiles : IPickFiles
    {
        private static readonly FilePickerFileType Jpegs = new("Фотографии JPEG")
        {
            Patterns = new[] { "*.jpg", "*.jpeg" },
            MimeTypes = new[] { "image/jpeg" }
        };

        private readonly Window? _owner;

        public AvaloniaPickFiles(Window? owner = null) => _owner = owner;

        /// <summary>
        /// The chosen files, or null when the dialog was dismissed. A dismissal is not a
        /// failure: the caller decides whether an empty answer means "do nothing".
        /// </summary>
        public async Task<string[]?> PickImagesAsync(
            string title = "Выберите JPEG файлы",
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var provider = Provider();
            if (provider is null || !provider.CanOpen) return null;

            var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = true,
                FileTypeFilter = new[] { Jpegs }
            });

            var paths = files
                .Select(f => f.TryGetLocalPath())
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .ToList();

            return paths.Count > 0 ? paths.ToArray() : null;
        }

        /// <summary>
        /// The provider, read on the UI thread. It hangs off a window rather than off the
        /// application, so before there is a window there is nothing to ask.
        /// </summary>
        private IStorageProvider? Provider()
        {
            var window = _owner ?? MainWindowOf();
            return window?.StorageProvider;
        }

        private static Window? MainWindowOf() =>
            (Avalonia.Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
    }
}