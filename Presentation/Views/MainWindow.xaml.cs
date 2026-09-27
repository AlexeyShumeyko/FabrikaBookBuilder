using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        /// Saves and then closes the project session. The editor is a working copy of one
        /// project: once it is saved the user is done with it, so the header takes them
        /// back to the project list, which reloads and shows the new state.
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

        // ------------------------------------------------------------------
        //  Header stubs: theme, help, contacts
        // ------------------------------------------------------------------

        /// <summary>
        /// Theme, help and contacts exist as buttons only.
        ///
        /// The owner asked for the finished silhouette in the header, and for nothing
        /// behind the buttons yet: the help section is being reviewed separately and there
        /// is only a light theme, so a switch would promise something that does not exist.
        /// Each one says so in the app's own dialog, in the same style as everything else -
        /// a dead button with no explanation is the kind of thing that gets reported as a
        /// bug.
        /// </summary>
        private void OnThemeClick(object sender, RoutedEventArgs e) => SayInDevelopment(
            "Тема оформления",
            "Светлая и тёмная темы появятся в следующих обновлениях.\n\nПока программа работает в светлой теме.");

        private void OnHelpClick(object sender, RoutedEventArgs e) => SayInDevelopment(
            "Помощь",
            "Раздел помощи готовится к переработке.\n\nОн будет доступен из этой кнопки и по клавише F1.");

        private void OnContactsClick(object sender, RoutedEventArgs e) => SayInDevelopment(
            "Контакты",
            "Раздел с контактами поддержки будет добавлен позже.");

        private static void SayInDevelopment(string title, string message)
            => Dialogs.AppDialogs.Notify(title, message, "Понятно");

        /// <summary>
        /// A click anywhere in the window commits a pending inline project rename.
        ///
        /// The owner types a new name, clicks "anywhere", and expects it to stick. That
        /// only worked by accident before, and differently in each mode: the editors relied
        /// on the rename box's LostFocus, which only fires when the click lands on
        /// something that takes keyboard focus. Click the books area and it took focus, so
        /// the name applied; click the control panel, the header, or a tab - none of which
        /// are focusable - and the box stayed open and the name was silently thrown away.
        ///
        /// The handler is on the WINDOW, not on the editor view, because the header and the
        /// tabs are the parts of the screen that are outside every view. Preview
        /// tunneling sees the click before the control underneath it, and the walk up the
        /// visual tree skips the clicks that are starting an edit of their own (a button,
        /// another text box) or landing inside the rename box itself.
        /// </summary>
        private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source) return;

            for (DependencyObject? node = source; node != null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is TextBox || node is Button) return;
            }

            switch (_viewModel.CurrentView)
            {
                case UniqueFoldersView unique:
                    unique.TryCommitPendingRename();
                    break;
                case CombinedModeView combined:
                    combined.TryCommitPendingRename();
                    break;
            }
        }

        private async System.Threading.Tasks.Task SaveCurrentProjectAsync()
        {
            bool saved = false;

            switch (_viewModel.CurrentMode)
            {
                case AppMode.UniqueFolders:
                    var unique = TryGet<UniqueFoldersViewModel>();
                    if (unique != null) saved = await unique.QuickSaveAsync();
                    break;

                case AppMode.Combined:
                    var combined = TryGet<CombinedModeViewModel>();
                    if (combined != null) saved = await combined.QuickSaveAsync();
                    break;
            }

            // Export does its own navigation through ProjectExported; save has none of
            // its own, and QuickSaveAsync reports whether anything was written.
            if (saved)
                _viewModel.EndSession();
        }
    }
}
