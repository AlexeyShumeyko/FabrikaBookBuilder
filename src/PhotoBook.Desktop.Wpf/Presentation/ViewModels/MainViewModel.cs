using PhotoBook.Presentation.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoBook.Core;
using PhotoBook.Application;
using PhotoBook.Application;

namespace PhotoBookRenamer.Presentation.ViewModels
{
    /// <summary>
    /// Navigation hub and we of the persistent top bar.
    ///
    /// Registered as a SINGLETON. That matters: the header must survive every
    /// screen change, and previously each child ViewModel resolved its own
    /// transient MainViewModel and overwrote <c>MainWindow.DataContext</c>,
    /// which would have torn the header down on every navigation.
    /// </summary>
    public class MainViewModel : ViewModelBase, IShellNavigator
    {
        private readonly UniqueFoldersViewModel _uniqueFolders;
        private readonly CombinedModeViewModel _combinedMode;

        private AppMode _currentMode = AppMode.StartScreen;
        private object? _currentView;

        /// <summary>
        /// Mode of the project the user currently has open, or null when no project is
        /// open. The two editor tabs stay visible at all times, but they are only
        /// reachable while this is set: a mode is a property of a project, not a screen
        /// of its own. Set by <see cref="OpenProject"/> (saved project) or
        /// <see cref="CreateProject"/> (mode card), cleared by <see cref="EndSession"/>.
        /// </summary>
        private AppMode? _openProjectMode;

        private readonly IProjectListService _projectListService;
        private readonly IProjectRepository _projects;
        private readonly IFileService _files;
        private readonly IProjectDialogs _dialogs;
        private readonly IScreenViewFactory _screens;

        public MainViewModel(
            UniqueFoldersViewModel uniqueFolders,
            CombinedModeViewModel combinedMode,
            IProjectListService projectListService,
            IProjectRepository projects,
            IProjectDialogs dialogs,
            IFileService files,
        IScreenViewFactory screens)
        {
            _dialogs = dialogs;
            _files = files;
            _screens = screens;
            _uniqueFolders = uniqueFolders;
            _combinedMode = combinedMode;
            _projectListService = projectListService;
            _projects = projects;

            // The editor VMs are singletons, so they outlive every view swap. Listening
            // to them keeps the header's project name / enabled states in sync.
            _uniqueFolders.PropertyChanged += Editor_PropertyChanged;
            _combinedMode.PropertyChanged += Editor_PropertyChanged;

            GoToProjectsCommand = new RelayCommand(() => CurrentMode = AppMode.ProjectList);
            GoToModeSelectCommand = new RelayCommand(() => CurrentMode = AppMode.StartScreen);

            // These two exist for the Ctrl+1 / Ctrl+2 bindings. They cannot bypass the
            // session check: the setter below refuses an editor mode with no open
            // project, so the shortcut cannot become a back door into a mode.
            GoToUniqueFoldersCommand = new RelayCommand(() => CurrentMode = AppMode.UniqueFolders);
            GoToCombinedModeCommand = new RelayCommand(() => CurrentMode = AppMode.Combined);

            // A finished export ends the session: the user is done with the project and
            // belongs back on the project list.
            _uniqueFolders.ProjectExported += (_, _) => EndSession();
            _combinedMode.ProjectExported += (_, _) => EndSession();

            // "Back" from either editor lands here too. The editors cannot be given a
            // reference to this object: it is built out of them, so that would be a
            // circle the container cannot resolve. They say what they want instead.
            _uniqueFolders.BackToProjectListRequested += () => EndSession();
            _combinedMode.BackToProjectListRequested += () => EndSession();

            // Legacy names kept for the old Ctrl+1 / Ctrl+2 bindings.
            SwitchToUniqueFoldersCommand = GoToModeSelectCommand;
            SwitchToCombinedModeCommand = GoToModeSelectCommand;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var projectListVm = NewProjectListViewModel();
                CurrentView = _screens.CreateScreen(AppMode.ProjectList, projectListVm);
                _currentMode = AppMode.ProjectList;
                OnPropertyChanged(nameof(CurrentMode));
            });
        }

        // ------------------------------------------------------------------
        //  Navigation
        // ------------------------------------------------------------------

        public AppMode CurrentMode
        {
            get => _currentMode;
            set
            {
                // Single choke point for the "a mode needs a project" rule. Every route
                // in - tab click, Ctrl+1 / Ctrl+2, the mode cards, the project list -
                // ends up here, and only OpenProject may enter an editor without one.
                if (value is AppMode.UniqueFolders or AppMode.Combined && !CanEnterMode(value))
                    return;

                // Remember where the user was: the screen being left is the one that was
                // preparing something for itself, and nobody is looking at it any more.
                var leaving = _currentMode;

                if (!SetProperty(ref _currentMode, value)) return;

                // Stopping that preparation is the difference between leaving a project in
                // a second and leaving it in half a minute of decoding for a screen that is
                // no longer on show. The editors ask for this themselves when they finish a
                // save or an export; this is the other direction, the person clicking
                // "Мои проекты" with the mouse.
                if (leaving != value && leaving is AppMode.UniqueFolders or AppMode.Combined)
                    StopPreparationOf(leaving);

                // Leaving an editor by tab or shortcut also ends the session, otherwise
                // the mode tab would stay enabled with no project behind it.
                if (value is AppMode.ProjectList or AppMode.StartScreen)
                    _openProjectMode = null;

                BuildView();
            }
        }

        private bool CanEnterMode(AppMode mode)
            => mode == _currentMode || mode == _openProjectMode;

        /// <summary>
        /// Opens an editor for a project: a mode card for a brand new project, or a saved
        /// project from the list. This is the only supported way into "Уникальные папки"
        /// and "Комбинированный".
        /// </summary>
        public void OpenProject(AppMode mode)
        {
            if (mode is not (AppMode.UniqueFolders or AppMode.Combined)) return;

            _openProjectMode = mode;
            CurrentMode = mode;
        }

        /// <summary>
        /// Same as <see cref="OpenProject"/> but for a project that does not exist yet, so
        /// the editor is emptied first. The editor ViewModels are singletons: without the
        /// reset, "Создать проект" carried the previous session's books and project record
        /// into the new project, and the first save overwrote the project the user had
        /// open before.
        /// </summary>
        public void CreateProject(AppMode mode)
        {
            if (mode is not (AppMode.UniqueFolders or AppMode.Combined)) return;

            if (mode == AppMode.UniqueFolders)
                _uniqueFolders.ResetProjectCommand.Execute(null);
            else
                _combinedMode.ResetProjectCommand.Execute(null);

            OpenProject(mode);
        }

        /// <summary>
        /// The project is saved or exported, so the editor is done: drop the session and
        /// go back to the project list, which reloads and shows the fresh state.
        /// </summary>
        public void EndSession()
        {
            _openProjectMode = null;
            CurrentMode = AppMode.ProjectList;
        }

        public object? CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }

        /// <summary>
        /// Puts the screen for <see cref="CurrentMode"/> on the window. The navigator decides
        /// which view model is on screen; the shell's factory decides what that looks like.
        /// </summary>
        /// <remarks>
        /// The pair is chosen here and the screen is built over it, in that order, because
        /// a project list is rebuilt on every visit: the view model has to exist before
        /// anything can wrap it.
        /// </remarks>
        private void BuildView()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                object? viewModel = _currentMode switch
                {
                    AppMode.StartScreen => this,
                    AppMode.ProjectList => NewProjectListViewModel(),
                    AppMode.UniqueFolders => _uniqueFolders,
                    AppMode.Combined => _combinedMode,
                    _ => null
                };

                // Help has no screen of its own - the help is a dialog - so a mode without
                // one leaves whatever was on the window untouched, as it always has.
                if (viewModel != null)
                {
                    CurrentView = _screens.CreateScreen(_currentMode, viewModel);

                    // Asked for after the view exists, so the cards fill a list that is
                    // already on the window rather than one that appears a moment later.
                    if (viewModel is ProjectListViewModel projectList)
                        projectList.LoadProjectsCommand.Execute(null);
                }

                RaiseHeaderChanged();
            });
        }

        // ------------------------------------------------------------------
        //  Header state
        // ------------------------------------------------------------------

        /// <summary>
        /// A fresh list, wired with what it needs. Built per visit rather than kept: it
        /// exists to show the list as it is at the moment it is opened.
        /// </summary>
        private ProjectListViewModel NewProjectListViewModel()
            => new(_projectListService, _projects, _dialogs, this, _files);

        private void StopPreparationOf(AppMode mode)
            => EditorFor(mode)?.StopPreparing();


        /// <summary>The editor that owns a mode, or null for the list and the mode screen.</summary>
        public IProjectEditor? EditorFor(AppMode mode) => mode switch
        {
            AppMode.UniqueFolders => _uniqueFolders,
            AppMode.Combined => _combinedMode,
            _ => null
        };

        /// <summary>
        /// Hands an opened project to the editor that owns its mode, once that editor can
        /// receive one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the shell's half of the hand-over, and it is the half specific to this
        /// engine: the editor view is created when the screen changes, and it needs a moment
        /// after that before its bindings are live and it can be given a project. The wait
        /// lives here, where the windows are known, rather than in the screen that asked -
        /// which is what lets that screen be written once and run in either engine.
        /// </para>
        /// <para>
        /// Two dispatches, not one, and that is deliberate. The first runs after the view
        /// exists; the second runs a Loaded-priority tick later, by which time the bindings are
        /// live. A single dispatch handed the editor its project before it could see it, and
        /// the project silently never appeared.
        /// </para>
        /// </remarks>
        public void HandProjectToEditor(Project project, ProjectInfo record)
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;

            dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() =>
                {
                    dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Loaded,
                        new Action(() =>
                        {
                            // The shell decides which editor owns the mode; the screen that
                            // asked does not get to decide for itself.
                            var editor = EditorFor(record.Mode);
                            if (editor == null) return;

                            editor.SetProject(project, record);
                        }));
                }));
        }

        /// <summary>The editor on the current screen, or null on list / mode-select.</summary>
        public IProjectEditor? ActiveEditor => EditorFor(_currentMode);

        public string ProjectTitle => _currentMode switch
        {
            AppMode.UniqueFolders => _uniqueFolders.CurrentProjectInfo?.Name ?? "Новый проект",
            AppMode.Combined => _combinedMode.CurrentProjectInfo?.Name ?? "Новый проект",
            _ => "Не выбран"
        };

        public bool HasOpenProject => _currentMode switch
        {
            AppMode.UniqueFolders => _uniqueFolders.CurrentProjectInfo != null,
            AppMode.Combined => _combinedMode.CurrentProjectInfo != null,
            _ => false
        };

        /// <summary>
        /// Header "Choose folders" / "Load photos". Only Combined still needs it: Unique
        /// Folders has the same action in its project panel, so the header copy was a
        /// duplicate.
        /// </summary>
        /// <summary>
        /// The top bar keeps only navigation and undo. Loading photos lives in the file
        /// list, saving and exporting in the project panel - the same split the unique
        /// mode uses, so the two screens do not teach the user two different habits.
        ///
        /// That used to be expressed as three visibility flags that were permanently
        /// false. The buttons they drove stayed in the markup, collapsed, waiting for a
        /// condition that could never arrive: a property whose value is a constant is not
        /// a switch, and the comment above it described a state the product has moved on
        /// from. The buttons and the flags are gone; the editors own their own actions.
        /// </summary>
        public bool ShowUndoRedo => _currentMode == AppMode.UniqueFolders;
        public bool CanUndo => ShowUndoRedo && _uniqueFolders.UndoCommand.CanExecute(null);
        public bool CanRedo => ShowUndoRedo && _uniqueFolders.RedoCommand.CanExecute(null);

        /// <summary>
        /// The two mode tabs stay in the header but are only clickable while their
        /// project is open. Reachable: the mode card for a new project, a saved project
        /// from the list, or the tab itself while already inside that mode.
        /// </summary>
        public bool CanOpenUniqueFoldersTab => _currentMode == AppMode.UniqueFolders || _openProjectMode == AppMode.UniqueFolders;

        public bool CanOpenCombinedTab => _currentMode == AppMode.Combined || _openProjectMode == AppMode.Combined;

        private void Editor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(UniqueFoldersViewModel.ProjectName)
                or nameof(UniqueFoldersViewModel.CurrentProjectInfo)
                or nameof(UniqueFoldersViewModel.Project)
                or nameof(CombinedModeViewModel.ProjectName)
                or nameof(CombinedModeViewModel.CurrentProjectInfo)
                or nameof(CombinedModeViewModel.Project))
            {
                RaiseHeaderChanged();
            }
        }

        private void RaiseHeaderChanged()
        {
            OnPropertyChanged(nameof(ActiveEditor));
            OnPropertyChanged(nameof(ProjectTitle));
            OnPropertyChanged(nameof(HasOpenProject));
            OnPropertyChanged(nameof(ShowUndoRedo));
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            OnPropertyChanged(nameof(CanOpenUniqueFoldersTab));
            OnPropertyChanged(nameof(CanOpenCombinedTab));
        }

        // ------------------------------------------------------------------
        //  Commands
        // ------------------------------------------------------------------

        public ICommand GoToProjectsCommand { get; }
        public ICommand GoToModeSelectCommand { get; }
        public ICommand GoToUniqueFoldersCommand { get; }
        public ICommand GoToCombinedModeCommand { get; }

        public ICommand SwitchToUniqueFoldersCommand { get; }
        public ICommand SwitchToCombinedModeCommand { get; }
    }
}
