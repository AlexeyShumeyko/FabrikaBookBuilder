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
using PhotoBookRenamer.Presentation.Views;

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

            // The options modal owns the copy so it can show a progress bar, and it runs
            // the export itself. This method only handles the surrounding bookkeeping.
            var options = new Presentation.Dialogs.ExportDialog(Project, _exportService);
            options.Owner = System.Windows.Application.Current.MainWindow;

            try
            {
                // No window veil here: the export dialog shows its own progress, and
                // a dark wash behind a modal is what the owner kept reporting as a
                // grey background leaking in.
                if (options.ShowDialog() != true || !options.Exported)
                    return;

                string outputFolder = options.ExportedFolder;

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
        private async Task ShowExportSuccessAsync(string outputFolder)
        {
            var dialog = new Presentation.Dialogs.ExportSuccessDialog(outputFolder);
            dialog.Owner = System.Windows.Application.Current.MainWindow;
            if (dialog.ShowDialog() == true && dialog.GoToFolder)
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start("explorer.exe", outputFolder);
                    }
                    catch
                    {
                        // Explorer failing to open is not worth surfacing.
                    }
                });
            }
        }

        /// <summary>
        /// Ctrl+Shift+S. Same modal as <see cref="ExportAsync"/> - the only difference
        /// used to be that the target folder was picked without a name prompt, which the
        /// new options modal handles itself.
        /// </summary>
        private async Task ExportWithFolderAsync()
        {
            if (Project == null) return;

            var options = new Presentation.Dialogs.ExportDialog(Project, _exportService);
            options.Owner = System.Windows.Application.Current.MainWindow;

            try
            {
                // No window veil here: the export dialog shows its own progress, and
                // a dark wash behind a modal is what the owner kept reporting as a
                // grey background leaking in.
                if (options.ShowDialog() != true || !options.Exported)
                    return;

                string outputFolder = options.ExportedFolder;

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
