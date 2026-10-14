using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using PhotoBook.Application;
using PhotoBook.Core;
using PhotoBook.Shell.Views;

namespace PhotoBook.Shell.Platform
{
    /// <summary>
    /// The windows an editor needs in order to ask something.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every one of these returns a real answer or fails loudly. A dialog that quietly
    /// answered "cancelled" would let someone believe they had exported when nothing had
    /// happened, which is the failure this replaced.
    /// </para>
    /// <para>
    /// The folder picker is the platform's own rather than a window written here. The other
    /// shell has its own browser - 292 lines of markup and 396 of code - because that is what
    /// a desktop application could use at the time. This engine ships one with the system
    /// conventions of the machine it is running on, which is both less code and a better
    /// answer on a machine whose language is not the product's. It is a visible difference
    /// from the reference and it is recorded as one.
    /// </para>
    /// </remarks>
    internal class AvaloniaEditorDialogs : IEditorDialogs
    {
        private readonly IExportService _export;

        public AvaloniaEditorDialogs(IExportService exportService) => _export = exportService;

        public async Task<string?> AskToExport(Project project)
        {
            var window = new ExportWindow(project, _export);
            return await window.ShowDialog<string>(OwnerOf(() => window));
        }

        public async Task ShowExportSucceeded(string outputFolder)
        {
            var window = new NotifyWindow(
                "Открыть папку",
                $"Готово. Файлы выгружены в папку:{Environment.NewLine}{outputFolder}",
                destructive: false);

            await window.ShowDialog<object>(OwnerOf(() => window));
        }

        public async Task<List<string>?> PickFolders(List<string> existingFolders)
        {
            var storage = MainWindow()?.StorageProvider;
            if (storage is null || !storage.CanOpen) return null;

            var picked = (await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Выберите папки с фотографиями",
                AllowMultiple = true
            })).ToList();

            return picked.Count == 0
                ? null
                : picked.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Select(p => p!).ToList();
        }

        public async Task<int?> AskPageNumber(int maxPageNumber)
        {
            var window = new PageNumberWindow(maxPageNumber);
            return await window.ShowDialog<int?>(OwnerOf(() => window));
        }

        private static Window? MainWindow() =>
            (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        /// <summary>
        /// The window a dialog is shown against.
        /// </summary>
        /// <remarks>
        /// This engine has no ownerless form of the call, and by the time anything asks for a
        /// dialog the main window exists - it is what opened the screen that is asking. The
        /// fallback exists so the code has one answer rather than two, and it is never
        /// reached in the running program.
        /// </remarks>
        private static Window OwnerOf(Func<Window?> fallback)
        {
            var main = MainWindow();
            if (main is not null) return main;

            var placeholder = fallback();
            if (placeholder is not null) return placeholder;

            throw new InvalidOperationException(
                "A dialog was opened before there was a window to open it against.");
        }
    }
}
