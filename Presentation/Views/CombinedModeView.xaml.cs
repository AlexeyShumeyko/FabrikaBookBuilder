using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PhotoBookRenamer.Presentation.ViewModels;

// "Page" exists both as our domain entity and as a WPF control; the entity always wins here.
using Page = PhotoBookRenamer.Domain.Page;

namespace PhotoBookRenamer.Presentation.Views
{
    public partial class CombinedModeView : UserControl
    {
        private readonly CombinedModeViewModel _viewModel;

        public CombinedModeView(CombinedModeViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
        }

        // ------------------------------------------------------------------
        //  Structure inputs
        // ------------------------------------------------------------------

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            // Digits only, so NumberOfBooks / SpreadsPerBook can never receive junk.
            if (new Regex("[^0-9]+").IsMatch(e.Text))
                e.Handled = true;
        }

        // ------------------------------------------------------------------
        //  Source pool
        // ------------------------------------------------------------------

        private void OnUploadAreaClick(object sender, MouseButtonEventArgs e)
            => _viewModel.LoadFilesCommand.Execute(null);

        /// <summary>Accepts photos dragged in from Windows Explorer.</summary>
        private void OnUploadAreaDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

            foreach (var file in files)
            {
                if (_viewModel.AvailableFiles.Contains(file)) continue;
                if (!_viewModel.AddExternalFile(file)) continue;
            }

            _viewModel.LoadThumbnailsForNewFiles();
        }

        private void OnFileItemMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element) return;
            // The row is bound to a file entry, not to a bare path: the name, the pixel
            // size and the status are all shown from the same object.
            if (element.DataContext is not Domain.PhotoFileInfo file) return;
            if (!File.Exists(file.Path)) return;

            DragDrop.DoDragDrop(element, file.Path, DragDropEffects.Copy);
        }

        // ------------------------------------------------------------------
        //  Slots
        // ------------------------------------------------------------------

        private void OnSlotDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.Text)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private void OnSlotDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;

            if (e.Data.GetData(DataFormats.Text) is not string filePath) return;
            if (!File.Exists(filePath)) return;
            if (sender is not FrameworkElement { DataContext: Page page }) return;

            _viewModel.DropFileOnSlot(page, filePath, DropAction.ThisBookOnly);
        }

        /// <summary>Clicking an empty slot opens a picker, as the original did.</summary>
        private void OnSlotClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: Page page }) return;
            if (!string.IsNullOrEmpty(page.SourcePath)) return;

            var dialog = new OpenFileDialog
            {
                Filter = "Изображения|*.jpg;*.jpeg;*.JPG;*.JPEG|All files|*.*",
                Title = "Выберите фото для слота",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
                _viewModel.DropFileOnSlot(page, dialog.FileName, DropAction.ThisBookOnly);
        }

        private void OnImageDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            if (sender is FrameworkElement { Tag: Page page })
                OpenFileInViewer(page.SourcePath);
        }

        private void OnMovePageLeftClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: Page page })
                _viewModel.MovePageLeftCommand.Execute(page);
        }

        private void OnMovePageRightClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: Page page })
                _viewModel.MovePageRightCommand.Execute(page);
        }

        private void OnDuplicateToAllClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: Page page })
                _viewModel.DuplicateToAllBooksCommand.Execute(page);
        }

        private void OnDeletePageClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: Page page })
                _viewModel.DeletePageCommand.Execute(page);
        }

        private void OpenFileInViewer(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть файл:\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ------------------------------------------------------------------
        //  Inline project rename
        // ------------------------------------------------------------------

        private void OnGridPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Committing the rename is deferred, because the click that dismisses it is
            // this very event. Skipped when the click lands on another control that is
            // about to take focus.
            if (e.OriginalSource is not DependencyObject source) return;

            var current = source;
            while (current != null)
            {
                if (current is TextBox || current is Button) return;
                current = VisualTreeHelper.GetParent(current);
            }

            if (ProjectNameTextBox.Visibility == Visibility.Visible)
                Dispatcher.BeginInvoke(new Action(CommitProjectName),
                    System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Commits a pending inline rename, if one is open. Called by the window so a click
        /// anywhere - the header, the tabs, the panel - applies the name, not only a click
        /// inside this view.
        /// </summary>
        public void TryCommitPendingRename()
        {
            if (ProjectNameTextBox.Visibility == Visibility.Visible)
                CommitProjectName();
        }
        private void OnProjectNameClick(object sender, MouseButtonEventArgs e)
        {
            BeginProjectRename();
        }

        /// <summary>Same entry point for the pencil button next to the project name.</summary>
        private void OnProjectNamePencilClick(object sender, RoutedEventArgs e)
        {
            BeginProjectRename();
        }

        private void BeginProjectRename()
        {
            ProjectNameTextBox.Text = _viewModel.ProjectName ?? string.Empty;
            ProjectNameTextBlock.Visibility = Visibility.Collapsed;
            ProjectNameTextBox.Visibility = Visibility.Visible;
            ProjectNameTextBox.Focus();
            ProjectNameTextBox.SelectAll();
        }

        private void OnProjectNameLostFocus(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ProjectNameTextBox.Visibility == Visibility.Visible)
                    CommitProjectName();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void OnProjectNameKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    CommitProjectName();
                    e.Handled = true;
                    break;

                case Key.Escape:
                    ShowProjectNameAsText();
                    e.Handled = true;
                    break;
            }
        }

        private void CommitProjectName()
        {
            ShowProjectNameAsText();

            var newName = ProjectNameTextBox.Text.Trim();
            if (newName.Length == 0 || newName == _viewModel.ProjectName) return;

            // Order matters. The header shows CurrentProjectInfo.Name, and the only thing
            // that makes it re-read is the PropertyChanged raised by the ProjectName setter.
            // Writing the info object first and the property second is what makes the top
            // bar follow the rename; the other way round the header re-reads the OLD name,
            // so renaming worked in the panel and not in the header.
            if (_viewModel.CurrentProjectInfo != null)
            {
                _viewModel.CurrentProjectInfo.Name = newName;
                _viewModel.CurrentProjectInfo.LastModified = DateTime.Now;
            }

            _viewModel.ProjectName = newName;

            _ = _viewModel.SaveProjectNameOnlyAsync();
        }

        private void ShowProjectNameAsText()
        {
            ProjectNameTextBox.Visibility = Visibility.Collapsed;
            ProjectNameTextBlock.Visibility = Visibility.Visible;
        }
    }
}
