using System;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoBookRenamer.Domain;
using PhotoBookRenamer.Presentation.ViewModels;

namespace PhotoBookRenamer.Presentation.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;

            SetupKeyboardShortcuts();
            SetupWindowSize();
        }

        // ------------------------------------------------------------------
        //  Layout
        // ------------------------------------------------------------------

        private void SetupWindowSize()
        {
            var workArea = SystemParameters.WorkArea;
            Width = Math.Min(1400, workArea.Width * 0.9);
            Height = Math.Min(900, workArea.Height * 0.9);
            MinWidth = 1100;
            MinHeight = 680;

            Left = (workArea.Width - Width) / 2;
            Top = (workArea.Height - Height) / 2;
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            InvalidateVisual();
            UpdateLayout();
        }

        // ------------------------------------------------------------------
        //  Global shortcuts
        // ------------------------------------------------------------------
        //  F1 (help) and Ctrl+1 / Ctrl+2 are declared as InputBindings in XAML.
        //  Everything below needs to reach whichever singleton editor ViewModel is
        //  active, which is why it is routed here instead of being a KeyBinding.
        //
        //  Ctrl+O          load photos (Combined) / load folders (Unique, with Shift)
        //  Ctrl+S          save project
        //  Ctrl+Shift+S    export, asking for a target folder
        //  Ctrl+E          reset project
        //  Ctrl+Z / Ctrl+Y undo / redo (Unique Folders)
        // ------------------------------------------------------------------

        private void SetupKeyboardShortcuts() => KeyDown += MainWindow_KeyDown;

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (!Keyboard.IsKeyDown(Key.LeftCtrl)) return;

            var unique = TryGet<UniqueFoldersViewModel>();
            var combined = TryGet<CombinedModeViewModel>();
            bool shift = Keyboard.IsKeyDown(Key.LeftShift);
            AppMode mode = _viewModel.CurrentMode;

            switch (e.Key)
            {
                case Key.O when mode == AppMode.Combined && !shift:
                    combined?.LoadFilesCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.O when mode == AppMode.UniqueFolders && shift:
                    unique?.LoadFoldersCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.S when shift && mode == AppMode.UniqueFolders:
                    unique?.ExportWithFolderCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.S when shift && mode == AppMode.Combined:
                    combined?.ExportWithFolderCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.S when !shift && mode == AppMode.UniqueFolders:
                    _ = SaveCurrentProjectAsync();
                    e.Handled = true;
                    break;

                case Key.S when !shift && mode == AppMode.Combined:
                    _ = SaveCurrentProjectAsync();
                    e.Handled = true;
                    break;

                case Key.E when mode == AppMode.UniqueFolders:
                    unique?.ResetProjectCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.E when mode == AppMode.Combined:
                    combined?.ResetProjectCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.Z when mode == AppMode.UniqueFolders:
                    unique?.UndoCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.Y when mode == AppMode.UniqueFolders:
                    unique?.RedoCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }

        private static T? TryGet<T>() where T : class
            => ((App)System.Windows.Application.Current).GetServiceProvider()?.GetRequiredService<T>();

        // ------------------------------------------------------------------
        //  Header actions
        // ------------------------------------------------------------------

        /// <summary>"Выбор папок" in Unique Folders, "Загрузить фото" in Combined.</summary>
        private void OnChooseSourceClick(object sender, RoutedEventArgs e)
        {
            switch (_viewModel.CurrentMode)
            {
                case AppMode.UniqueFolders:
                    TryGet<UniqueFoldersViewModel>()?.LoadFoldersCommand.Execute(null);
                    break;
                case AppMode.Combined:
                    TryGet<CombinedModeViewModel>()?.LoadFilesCommand.Execute(null);
                    break;
            }
        }

        /// <summary>
        /// Silent save with no navigation. The legacy SaveProjectCommand navigates back to
        /// the project list, which is wrong for a header button.
        /// </summary>
        private async void OnSaveClick(object sender, RoutedEventArgs e) => await SaveCurrentProjectAsync();

        private void OnExportClick(object sender, RoutedEventArgs e)
        {
            switch (_viewModel.CurrentMode)
            {
                case AppMode.UniqueFolders:
                    TryGet<UniqueFoldersViewModel>()?.ExportCommand.Execute(null);
                    break;
                case AppMode.Combined:
                    TryGet<CombinedModeViewModel>()?.ExportCommand.Execute(null);
                    break;
            }
        }

        private async System.Threading.Tasks.Task SaveCurrentProjectAsync()
        {
            switch (_viewModel.CurrentMode)
            {
                case AppMode.UniqueFolders:
                    var unique = TryGet<UniqueFoldersViewModel>();
                    if (unique != null) await unique.QuickSaveAsync();
                    break;

                case AppMode.Combined:
                    var combined = TryGet<CombinedModeViewModel>();
                    if (combined != null) await combined.QuickSaveAsync();
                    break;
            }
        }
    }
}
