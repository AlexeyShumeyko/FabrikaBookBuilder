using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhotoBookRenamer.Domain;
using PhotoBookRenamer.Presentation.ViewModels;

namespace PhotoBookRenamer.Presentation.Views
{
    public partial class StartScreenView : UserControl
    {
        private readonly MainViewModel _viewModel;

        public StartScreenView(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
        }

        /// <summary>
        /// Goes straight to the editor. The project record is created lazily on the first
        /// data-bearing action inside the editor (see EnsureProjectInfoAsync), so merely
        /// looking at this screen no longer leaves an empty "Черновик" in the project list.
        /// </summary>
        private void OnUniqueFoldersClick(object sender, MouseButtonEventArgs e)
        {
            if (ClickLandedOnInnerButton(e)) return;
            _viewModel.CreateProject(AppMode.UniqueFolders);
        }

        private void OnCombinedModeClick(object sender, MouseButtonEventArgs e)
        {
            if (ClickLandedOnInnerButton(e)) return;
            _viewModel.CreateProject(AppMode.Combined);
        }

        /// <summary>
        /// MouseLeftButtonDown on a card also fires for clicks on child controls. Without
        /// this guard a single click could both activate a nested button and navigate.
        /// </summary>
        private static bool ClickLandedOnInnerButton(MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source) return false;

            var current = source;
            while (current != null)
            {
                if (current is Button) return true;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return false;
        }
    }
}
