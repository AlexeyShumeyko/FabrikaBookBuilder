using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using PhotoBook.Application;
using PhotoBook.Core;

namespace PhotoBookRenamer.Presentation.Dialogs
{
    /// <summary>
    /// The WPF answer to <see cref="IEditorDialogs"/>: this shell's four windows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Making a window modal also means tying it to the window it was raised from,
    /// which used to be the view model's job - a line reading
    /// <c>Application.Current.MainWindow</c> in a class that was supposed to be about a
    /// project. Here it is one line, next to the construction.
    /// </para>
    /// <para>
    /// Opening a folder also moved here. The view model used to launch Explorer itself,
    /// which is a piece of platform rather than a decision about a project, and it is the
    /// kind of line that has no meaning at all in a shell where "open a folder" is something
    /// else. Failures are swallowed: Explorer not opening a window is not worth a message.
    /// </para>
    /// </remarks>
    internal class WpfEditorDialogs : IEditorDialogs
    {
        private readonly IFileService _files;
        private readonly IExportService _exports;

        public WpfEditorDialogs(IFileService files, IExportService exports)
        {
            _files = files;
            _exports = exports;
        }

        public Task<string?> AskToExport(Project project)
        {
            var dialog = new ExportDialog(project, _exports);
            Owner(dialog);

            return Task.FromResult(
                dialog.ShowDialog() == true && dialog.Exported ? dialog.ExportedFolder : null);
        }

        public async Task ShowExportSucceeded(string outputFolder)
        {
            var dialog = new ExportSuccessDialog(outputFolder);
            Owner(dialog);
            if (dialog.ShowDialog() != true || !dialog.GoToFolder) return;

            await Task.Run(() =>
            {
                try
                {
                    Process.Start("explorer.exe", outputFolder);
                }
                catch
                {
                    // Explorer failing to open is not worth surfacing.
                }
            });
        }

        public List<string>? PickFolders(List<string> existingFolders)
        {
            var dialog = new MultiFolderBrowserDialog(_files, existingFolders);
            Owner(dialog);

            if (dialog.ShowDialog() != true) return null;
            return dialog.SelectedFolders;
        }

        public int? AskPageNumber(int maxPageNumber)
        {
            var dialog = new PageNumberDialog(maxPageNumber);
            Owner(dialog);

            return dialog.ShowDialog() == true ? dialog.SelectedPageNumber : null;
        }

        /// <summary>
        /// Makes the dialog modal to the main window. Skipped when it already is, because a
        /// window cannot own itself.
        /// </summary>
        private static void Owner(Window dialog)
        {
            var owner = System.Windows.Application.Current?.MainWindow;
            if (owner != null && !ReferenceEquals(owner, dialog))
                dialog.Owner = owner;
        }
    }
}
