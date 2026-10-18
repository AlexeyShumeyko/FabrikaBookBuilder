using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
    /// The folder picker is a panel over the window rather than a second window, and it is
    /// written here rather than handed to the platform. The platform ships one and it does one
    /// thing: it opens a file list. It cannot show what is already chosen, and it cannot say
    /// how many photographs a folder holds before you commit to it - and both of those are the
    /// question. Choosing photographs for a book is the work, and it deserves a panel that
    /// answers it rather than a file list that asks for it again.
    /// </para>
    /// </remarks>
    internal class AvaloniaEditorDialogs : IEditorDialogs
    {
        private readonly IExportService _export;
        private readonly IFileService _files;

        public AvaloniaEditorDialogs(IExportService exportService, IFileService fileService)
        {
            _export = exportService;
            _files = fileService;
        }

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
            // A panel over the page rather than a second window. A frameless white window on a
            // white page had no edge to say it was on top, and dimming the page says it for it.
            var main = MainWindow() as MainWindow;
            if (main is null)
            {
                throw new InvalidOperationException(
                    "Проводник папок открывается поверх окна программы, а окна нет.");
            }

            return await main.ShowSheetAsync(new FolderBrowserView(_files, existingFolders));
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
