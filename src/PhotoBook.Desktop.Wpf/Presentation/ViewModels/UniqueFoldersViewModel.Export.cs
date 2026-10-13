using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoBook.Core;
using PhotoBook.Application;
using PhotoBook.Infrastructure;

namespace PhotoBookRenamer.Presentation.ViewModels
{
    /// <summary>Writing out the files the photographer asked for.</summary>
    public partial class UniqueFoldersViewModel : ViewModelBase, IProjectEditor
    {

        private async Task ExportAsync()
        {
            if (Project == null) return;

            // Save FIRST, for the same reason as in the combined mode: the export used to
            // mark the project "ready to print" in the index and end the session without
            // ever writing the project file, so a finished project reopened empty.
            await SaveProjectBeforeExportAsync();

            // The options window owns the progress bar, so it owns the work; this method
            // only handles the surrounding bookkeeping. Null comes back when the user stopped
            // before exporting.
            var outputFolder = await _editorDialogs.AskToExport(Project);

            try
            {
                // No window veil here: the export dialog shows its own progress, and
                // a dark wash behind a modal is what we kept reporting as a
                // grey background leaking in.
                if (outputFolder == null)
                    return;

                Project.OutputFolder = outputFolder;
                if (CurrentProjectInfo != null)
                {
                    CurrentProjectInfo.Status = ProjectStatus.SuccessfullyCompleted;
                    // PageCount is the number of spreads in ONE book, not a sum.
                    CurrentProjectInfo.PageCount =
                        Project.Books?.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                    await _projectListService.SaveProjectInfoAsync(CurrentProjectInfo);
                }

                ErrorMessage = null;
                await ShowExportSuccessAsync(outputFolder);
                ProjectExported?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка экспорта: {ex.Message}";
                _loggingService.LogError("Ошибка экспорта", ex);
            }
        }

        /// <summary>Success modal, with an optional "open the folder" action.</summary>
        private Task ShowExportSuccessAsync(string outputFolder) => _editorDialogs.ShowExportSucceeded(outputFolder);

        /// <summary>
        /// Ctrl+Shift+S. Same modal as <see cref="ExportAsync"/> - the only difference
        /// used to be that the target folder was picked without a name prompt, which the
        /// new options modal handles itself.
        /// </summary>
        private async Task ExportWithFolderAsync()
        {
            if (Project == null) return;

            var outputFolder = await _editorDialogs.AskToExport(Project);

            try
            {
                // No window veil here: the export dialog shows its own progress, and
                // a dark wash behind a modal is what we kept reporting as a
                // grey background leaking in.
                if (outputFolder == null)
                    return;

                Project.OutputFolder = outputFolder;
                if (CurrentProjectInfo != null)
                {
                    CurrentProjectInfo.Status = ProjectStatus.SuccessfullyCompleted;
                    CurrentProjectInfo.PageCount =
                        Project.Books?.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
                    await _projectListService.SaveProjectInfoAsync(CurrentProjectInfo);
                }

                ErrorMessage = null;
                await ShowExportSuccessAsync(outputFolder);
                ProjectExported?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка экспорта: {ex.Message}";
                _loggingService.LogError("Ошибка экспорта", ex);
            }
        }
    }
}
