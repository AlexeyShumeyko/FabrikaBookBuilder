using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PhotoBookRenamer.Domain;
using PhotoBookRenamer.Presentation.ViewModels;

// "Page" exists both as our domain entity and as a WPF control; the entity always wins here.
using Page = PhotoBookRenamer.Domain.Page;

namespace PhotoBookRenamer.Presentation.Views
{
    public partial class UniqueFoldersView : UserControl
    {
        private readonly UniqueFoldersViewModel _viewModel;

        public UniqueFoldersView(UniqueFoldersViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
        }

        // ------------------------------------------------------------------
        //  Slot actions
        // ------------------------------------------------------------------

        private void OnMovePageLeftClick(object sender, RoutedEventArgs e)
            => RunPageCommand(sender, page => _viewModel.MovePageUpCommand.Execute(page));

        private void OnMovePageRightClick(object sender, RoutedEventArgs e)
            => RunPageCommand(sender, page => _viewModel.MovePageDownCommand.Execute(page));

        private void OnAssignPageNumberClick(object sender, RoutedEventArgs e)
            => RunPageCommand(sender, page => _viewModel.AssignPageNumberCommand.Execute(page));

        /// <summary>Promotes the clicked spread to be the book's cover.</summary>
        private void OnSetCoverClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { Tag: Page page })
                _viewModel.AssignCoverCommand.Execute(page);
        }

        /// <summary>
        /// "Заменить" on the cover. Reuses SelectCoverCommand, which opens a file picker
        /// and assigns the chosen file to the owning book's cover.
        /// </summary>
        private void OnReplaceClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: Page page }) return;

            var book = _viewModel.Books.FirstOrDefault(b => b.Cover == page);
            if (book != null)
                _viewModel.SelectCoverCommand.Execute(book);
        }

        private void RunPageCommand(object sender, Action<Page> action)
        {
            if (sender is FrameworkElement { Tag: Page page })
                action(page);
        }

        // ------------------------------------------------------------------
        //  Preview
        // ------------------------------------------------------------------

        private void OnPageImageDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            if (sender is FrameworkElement { Tag: Page page })
                OpenFileInViewer(page.SourcePath);
        }

        private void OpenFileInViewer(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) return;

            try
            {
                Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true });
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

        private void OnProjectNameClick(object sender, MouseButtonEventArgs e)
        {
            ProjectNameTextBox.Text = _viewModel.ProjectName ?? string.Empty;
            ProjectNameTextBlock.Visibility = Visibility.Collapsed;
            ProjectNameTextBox.Visibility = Visibility.Visible;
            ProjectNameTextBox.Focus();
            ProjectNameTextBox.SelectAll();
        }

        private void OnProjectNameLostFocus(object sender, RoutedEventArgs e)
        {
            // Deferred: LostFocus also fires while we are still typing.
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
            if (newName.Length == 0) return;
            if (newName == _viewModel.ProjectName) return;

            _viewModel.ProjectName = newName;
            if (_viewModel.CurrentProjectInfo != null)
            {
                _viewModel.CurrentProjectInfo.Name = newName;
                _viewModel.CurrentProjectInfo.LastModified = DateTime.Now;
            }

            _ = _viewModel.SaveProjectNameOnlyAsync();
        }

        private void ShowProjectNameAsText()
        {
            ProjectNameTextBox.Visibility = Visibility.Collapsed;
            ProjectNameTextBlock.Visibility = Visibility.Visible;
        }
    }
}
