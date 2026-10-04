using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PhotoBook.Core;
using PhotoBookRenamer.Presentation.ViewModels;

// "Page" exists both as our domain entity and as a WPF control; the entity always wins here.
using Page = PhotoBook.Core.Page;

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

        /// <summary>
        /// Footer action on a spread card. The footer is a single TextBlock that swaps its
        /// label on hover, so the click has to work out the intent from the page itself:
        /// a spread gets promoted to cover, the cover opens a file picker to be replaced.
        /// </summary>
        private void OnFooterActionClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: Page page }) return;

            if (!page.IsCover)
            {
                _viewModel.AssignCoverCommand.Execute(page);
                return;
            }

            var book = _viewModel.Books.FirstOrDefault(b => b.Cover == page);
            if (book != null)
                _viewModel.SelectCoverCommand.Execute(book);
        }

        private void RunPageCommand(object sender, Action<Page> action)
        {
            if (sender is FrameworkElement { Tag: Page page })
                action(page);
        }

        /// <summary>
        /// "Сбросить порядок" in a book header. The Tag is the Book, so the command only
        /// re-sorts that one book.
        /// </summary>
        private void OnResetBookOrderClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: Book book })
                _viewModel.ResetBookOrderCommand.Execute(book);
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
            BeginProjectRename();
        }

        /// <summary>Same entry point for the pencil button next to the project name.</summary>
        private void OnProjectNamePencilClick(object sender, RoutedEventArgs e)
        {
            BeginProjectRename();
        }

        /// <summary>
        /// Commits a pending inline rename, if one is open. Called by the window, because
        /// "click anywhere and the name applies" includes the header and the tabs, which
        /// live outside this view - a handler on the view alone would only see clicks that
        /// land inside it, which is the half of the screen where it already worked.
        /// </summary>
        public void TryCommitPendingRename()
        {
            if (ProjectNameTextBox.Visibility == Visibility.Visible)
                CommitProjectName();
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
