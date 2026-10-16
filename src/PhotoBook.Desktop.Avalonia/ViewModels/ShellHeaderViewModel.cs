using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using PhotoBook.Core;
using PhotoBook.Shell.Navigation;

// Two classes in the solution are called ObservableObject. The toolkit's is named here so
// the choice is visible at the point of use rather than left to whichever one an import
// list happens to reach first.
using ObservableObject = CommunityToolkit.Mvvm.ComponentModel.ObservableObject;

namespace PhotoBook.Shell.ViewModels
{
    /// <summary>
    /// The header's state: which screen is showing, which tabs are openable, and the name of
    /// whatever project is open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It holds the navigator and nothing else, and every value here is read straight off the
    /// navigator. There is no state of its own to fall out of step - a tab cannot say it is
    /// selected while the screen behind it says otherwise, because it is asking the same place
    /// the screen is.
    /// </para>
    /// <para>
    /// It exists in this shell rather than beside the other view models because the header is
    /// the shell's own: it is drawn by the window, it navigates between screens, and none of
    /// that is true of anything a screen shows.
    /// </para>
    /// </remarks>
    public sealed partial class ShellHeaderViewModel : ObservableObject
    {
        private readonly AvaloniaNavigator _navigator;

        public ShellHeaderViewModel(AvaloniaNavigator navigator)
        {
            _navigator = navigator;
            _navigator.ModeChanged += (_, _) => Refresh();
            _navigator.ProjectChanged += (_, _) => Refresh();
            _navigator.ProjectNameChanged += (_, _) => Refresh();
        }

        /// <summary>Which screen is showing. Setting it is how a tab navigates.</summary>
        public AppMode CurrentMode
        {
            get => _navigator.CurrentMode;
            set => _navigator.CurrentMode = value;
        }

        public bool IsProjectsSelected => CurrentMode == AppMode.ProjectList;

        public bool IsStartSelected => CurrentMode == AppMode.StartScreen;

        public bool IsUniqueFoldersSelected => CurrentMode == AppMode.UniqueFolders;

        public bool IsCombinedSelected => CurrentMode == AppMode.Combined;

        /// <summary>
        /// An editor is reachable while its project is open, and unreachable otherwise.
        /// </summary>
        /// <remarks>
        /// A mode is reached through a project, not by choosing a tab, so the two editor tabs
        /// are greyed until something has been opened. The current screen counts as reachable
        /// even after the session has ended: leaving an editor without a project behind it
        /// would leave the tab disabled while its own screen is on display.
        /// </remarks>
        public bool CanOpenUniqueFoldersTab => IsReachable(AppMode.UniqueFolders);

        public bool CanOpenCombinedTab => IsReachable(AppMode.Combined);

        /// <summary>What the header says after "Проект:".</summary>
        public string ProjectTitle => CurrentMode switch
        {
            AppMode.UniqueFolders => NameOf(AppMode.UniqueFolders),
            AppMode.Combined => NameOf(AppMode.Combined),
            _ => "Не выбран"
        };

        /// <summary>
        /// Whether undo and redo belong on screen at all.
        /// </summary>
        /// <remarks>
        /// Only the folders editor has a history worth showing. In the combined editor undo
        /// exists too, but the panel there has its own controls for the things a person
        /// changes most, and two sets of arrows on one screen is one more set to read.
        /// </remarks>
        public bool ShowUndoRedo => CurrentMode == AppMode.UniqueFolders;

        /// <summary>
        /// The editor's own undo, rather than a copy of it here.
        /// </summary>
        /// <remarks>
        /// A wrapper would have to re-ask whether the editor can undo it every time anything
        /// changed, and the editor already knows. Handing the command straight over keeps the
        /// button enabled exactly when the editor says it can be.
        /// </remarks>
        public ICommand? UndoCommand => _navigator.EditorFor(AppMode.UniqueFolders)?.UndoCommand;

        public ICommand? RedoCommand => _navigator.EditorFor(AppMode.UniqueFolders)?.RedoCommand;

        [RelayCommand]
        private void GoToProjects() => CurrentMode = AppMode.ProjectList;

        [RelayCommand]
        private void GoToModeSelect() => CurrentMode = AppMode.StartScreen;

        [RelayCommand]
        private void GoToUniqueFolders() => CurrentMode = AppMode.UniqueFolders;

        [RelayCommand]
        private void GoToCombined() => CurrentMode = AppMode.Combined;

        private bool IsReachable(AppMode mode) => CurrentMode == mode || _navigator.SessionMode == mode;

        private string NameOf(AppMode mode)
            => _navigator.EditorFor(mode)?.OpenProject?.Name ?? "Новый проект";

        private void Refresh()
        {
            OnPropertyChanged(nameof(CurrentMode));
            OnPropertyChanged(nameof(IsProjectsSelected));
            OnPropertyChanged(nameof(IsStartSelected));
            OnPropertyChanged(nameof(IsUniqueFoldersSelected));
            OnPropertyChanged(nameof(IsCombinedSelected));
            OnPropertyChanged(nameof(CanOpenUniqueFoldersTab));
            OnPropertyChanged(nameof(CanOpenCombinedTab));
            OnPropertyChanged(nameof(ProjectTitle));
            OnPropertyChanged(nameof(ShowUndoRedo));
            OnPropertyChanged(nameof(UndoCommand));
            OnPropertyChanged(nameof(RedoCommand));
        }
    }
}
