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
using PhotoBookRenamer.Presentation.Converters;
using PhotoBookRenamer.Presentation.Dialogs;

namespace PhotoBookRenamer.Presentation.ViewModels
{
    /// <summary>Writing out the files the photographer asked for.</summary>
    public partial class CombinedModeViewModel : ViewModelBase, IProjectEditor
    {

        private async Task LoadThumbnailForPage(Page page)
        {
            if (string.IsNullOrEmpty(page.SourcePath)) return;
        }

        private async Task ExportAsync()
        {
            if (Project == null) return;

            // Combined mode must not silently skip empty slots the way Unique Folders
            // mode does, so the validation stays here - before the options modal opens.
            var missingSlots = new List<string>();
            foreach (var book in Books)
            {
                foreach (var p in book.Pages.Where(p => p.IsEmpty))
                    missingSlots.Add($"Книга {book.BookIndex}, разворот {p.Index}");
            }

            if (missingSlots.Count > 0)
            {
                var list = string.Join("\n", missingSlots.Take(12));
                if (missingSlots.Count > 12) list += $"\n... и ещё {missingSlots.Count - 12}";
                System.Windows.MessageBox.Show($"Не все развороты заполнены:\n\n{list}",
                    "Предупреждение", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            await RunExportAsync();
        }

        /// <summary>
        /// Ctrl+Shift+S. Validates covers as well, which the plain export does not, and
        /// otherwise shares the same options modal.
        /// </summary>
        private async Task ExportWithFolderAsync()
        {
            if (Project == null) return;

            var missingCovers = Books
                .Where(b => b.Cover == null || b.Cover.IsEmpty)
                .Select(b => $"Книга {b.BookIndex}: не назначена обложка")
                .ToList();

            var missingPages = new List<string>();
            foreach (var book in Books)
            {
                foreach (var p in book.Pages.Where(p => p.IsEmpty))
                    missingPages.Add($"Книга {book.BookIndex}, разворот {p.Index}");
            }

            if (missingCovers.Count > 0 || missingPages.Count > 0)
            {
                var parts = new List<string>();
                if (missingCovers.Count > 0)
                {
                    parts.Add("Не назначены обложки:\n" + string.Join("\n", missingCovers.Take(10)));
                }
                if (missingPages.Count > 0)
                {
                    var list = string.Join("\n", missingPages.Take(10));
                    if (missingPages.Count > 10) list += $"\n... и ещё {missingPages.Count - 10}";
                    parts.Add("Не заполнены развороты:\n" + list);
                }

                System.Windows.MessageBox.Show("Перед экспортом необходимо заполнить все обязательные поля:\n\n"
                                + string.Join("\n\n", parts),
                    "Предупреждение", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            await RunExportAsync();
        }

        /// <summary>
        /// Opens the options modal, which performs the copy, then updates the project
        /// status and shows the success dialog.
        /// </summary>
        private async Task RunExportAsync()
        {
            if (Project == null) return;

            // Save FIRST. The export used to update the index to "ready to print" and end
            // the session without ever writing the project file, so the list showed a
            // finished project that reopened empty - a critical data loss dressed as a
            // success. The copy works on the in-memory project; the save is what makes the
            // result survive, and it has to happen before anything navigates away.
            await SaveProjectBeforeExportAsync();

            var options = new Presentation.Dialogs.ExportDialog(Project, _exportService);
            options.Owner = System.Windows.Application.Current.MainWindow;

            IsLoading = true;
            try
            {
                if (options.ShowDialog() != true || !options.Exported)
                    return;

                string outputFolder = options.ExportedFolder;

                Project.OutputFolder = outputFolder;
                if (CurrentProjectInfo != null)
                {
                    CurrentProjectInfo.Status = ProjectStatus.SuccessfullyCompleted;
                    CurrentProjectInfo.PageCount = Books.FirstOrDefault()?.Pages?.Count(p => !p.IsCover) ?? 0;
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
            finally
            {
                IsLoading = false;
            }
        }

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

        private void DeleteFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            AvailableFiles.Remove(filePath);
        }
    }
}
