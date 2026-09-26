using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoBookRenamer.Domain;
using PhotoBookRenamer.Application;
using PhotoBookRenamer.Presentation.Views;

namespace PhotoBookRenamer.Presentation.ViewModels
{
    /// <summary>
    /// Navigation hub and owner of the persistent top bar.
    ///
    /// Registered as a SINGLETON. That matters: the header must survive every
    /// screen change, and previously each child ViewModel resolved its own
    /// transient MainViewModel and overwrote <c>MainWindow.DataContext</c>,
    /// which would have torn the header down on every navigation.
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        /// <summary>
        /// The in-app help system (HelpView / HelpViewModel / HelpSection) is fully
        /// intact but currently not reachable. Flip this to true to bring the
        /// header "?" button and the F1 binding back.
        /// </summary>
        public const bool HelpVisible = false;

        private readonly UniqueFoldersViewModel _uniqueFolders;
        private readonly CombinedModeViewModel _combinedMode;

        private AppMode _currentMode = AppMode.StartScreen;
        private AppMode? _previousModeBeforeHelp;
        private object? _currentView;
        private bool _isReturningFromHelp;

        /// <summary>
        /// Mode of the project the user currently has open, or null when no project is
        /// open. The two editor tabs stay visible at all times, but they are only
        /// reachable while this is set: a mode is a property of a project, not a screen
        /// of its own. Set by <see cref="StartSession"/> (mode card, saved project) and
        /// cleared by <see cref="EndSession"/>.
        /// </summary>
        private AppMode? _openProjectMode;

        public HelpSection? HelpSection { get; private set; }

        public MainViewModel(UniqueFoldersViewModel uniqueFolders, CombinedModeViewModel combinedMode)
        {
            _uniqueFolders = uniqueFolders;
            _combinedMode = combinedMode;

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

            // Legacy names kept for the old Ctrl+1 / Ctrl+2 bindings.
            SwitchToUniqueFoldersCommand = GoToModeSelectCommand;
            SwitchToCombinedModeCommand = GoToModeSelectCommand;

            OpenHelpCommand = new RelayCommand(() => OpenHelp());

            // По умолчанию показываем список всех проектов
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var serviceProvider = ((App)System.Windows.Application.Current).GetServiceProvider();
                if (serviceProvider != null)
                {
                    var projectListService = serviceProvider.GetRequiredService<IProjectListService>();
                    var projectListVm = new ProjectListViewModel(projectListService);
                    CurrentView = new ProjectListView(projectListVm);
                    _currentMode = AppMode.ProjectList;
                    OnPropertyChanged(nameof(CurrentMode));
                }
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
                // ends up here, and only StartSession may enter an editor without one.
                if (value is AppMode.UniqueFolders or AppMode.Combined && !CanEnterMode(value))
                    return;

                if (!SetProperty(ref _currentMode, value)) return;

                // Leaving an editor by tab or shortcut also ends the session, otherwise
                // the mode tab would stay enabled with no project behind it.
                if (value is AppMode.ProjectList or AppMode.StartScreen)
                    _openProjectMode = null;

                // Если мы возвращаемся из помощи, не создаем новый View
                if (_isReturningFromHelp)
                {
                    _isReturningFromHelp = false;
                    return;
                }

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
        public void StartSession(AppMode mode)
        {
            if (mode is not (AppMode.UniqueFolders or AppMode.Combined)) return;

            _openProjectMode = mode;
            CurrentMode = mode;
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

        /// <summary>Creates the View for <see cref="CurrentMode"/>. Single source of truth.</summary>
        private void BuildView()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var serviceProvider = ((App)System.Windows.Application.Current).GetServiceProvider();
                if (serviceProvider == null) return;

                switch (_currentMode)
                {
                    case AppMode.StartScreen:
                        CurrentView = new StartScreenView(this);
                        break;

                    case AppMode.ProjectList:
                        var projectListService = serviceProvider.GetRequiredService<IProjectListService>();
                        var projectListVm = new ProjectListViewModel(projectListService);
                        CurrentView = new ProjectListView(projectListVm);
                        // Обновляем список проектов при возврате
                        projectListVm.LoadProjectsCommand.Execute(null);
                        break;

                    case AppMode.UniqueFolders:
                        // ViewModel - Singleton, поэтому состояние редактирования сохраняется
                        CurrentView = new UniqueFoldersView(_uniqueFolders);
                        break;

                    case AppMode.Combined:
                        CurrentView = new CombinedModeView(_combinedMode);
                        break;

                    case AppMode.Help:
                        CurrentView = new HelpView(new HelpViewModel(HelpSection ?? Domain.HelpSection.Overview));
                        break;
                }

                RaiseHeaderChanged();
            });
        }

        // ------------------------------------------------------------------
        //  Header state
        // ------------------------------------------------------------------

        /// <summary>The editor VM for the current screen, or null on list / mode-select.</summary>
        public object? ActiveEditor => _currentMode switch
        {
            AppMode.UniqueFolders => _uniqueFolders,
            AppMode.Combined => _combinedMode,
            _ => null
        };

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

        /// <summary>Header "Choose folders" only makes sense inside the two editors.</summary>
        public bool ShowSourcePicker => _currentMode is AppMode.UniqueFolders or AppMode.Combined;

        /// <summary>Context label: Unique Folders picks folders, Combined picks photos.</summary>
        public string SourcePickerLabel => _currentMode == AppMode.Combined ? "Загрузить фото" : "Выбор папок";

        public bool CanSave => ActiveEditor != null;

        public bool CanExport => _currentMode switch
        {
            AppMode.UniqueFolders => _uniqueFolders.Project?.IsValid ?? false,
            AppMode.Combined => _combinedMode.Project?.IsValid ?? false,
            _ => false
        };

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

        /// <summary>
        /// "Сохранить" and "Экспорт" only make sense with a project in front of the user.
        /// On the project list and the mode-select screen they were dead buttons
        /// (CanSave / CanExport are already false there), so they are hidden instead.
        /// </summary>
        public bool ShowProjectActions => _currentMode is AppMode.UniqueFolders or AppMode.Combined;

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
            OnPropertyChanged(nameof(ShowSourcePicker));
            OnPropertyChanged(nameof(SourcePickerLabel));
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(CanExport));
            OnPropertyChanged(nameof(ShowUndoRedo));
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            OnPropertyChanged(nameof(CanOpenUniqueFoldersTab));
            OnPropertyChanged(nameof(CanOpenCombinedTab));
            OnPropertyChanged(nameof(ShowProjectActions));
        }

        // ------------------------------------------------------------------
        //  Commands
        // ------------------------------------------------------------------

        public ICommand GoToProjectsCommand { get; }
        public ICommand GoToModeSelectCommand { get; }
        public ICommand GoToUniqueFoldersCommand { get; }
        public ICommand GoToCombinedModeCommand { get; }
        public ICommand OpenHelpCommand { get; }

        public ICommand SwitchToUniqueFoldersCommand { get; }
        public ICommand SwitchToCombinedModeCommand { get; }

        // ------------------------------------------------------------------
        //  Help
        // ------------------------------------------------------------------

        public void OpenHelp(HelpSection? section = null)
        {
            // Сохраняем текущий режим перед переходом в помощь.
            // ViewModel редактора - Singleton, поэтому состояние не теряется.
            _previousModeBeforeHelp = _currentMode;
            HelpSection = section;
            CurrentMode = AppMode.Help;
        }

        public void ReturnFromHelp()
        {
            if (_previousModeBeforeHelp.HasValue)
            {
                var previousMode = _previousModeBeforeHelp.Value;
                _previousModeBeforeHelp = null;

                // Обходим сеттер, чтобы он не создал View дважды
                _isReturningFromHelp = true;
                _currentMode = previousMode;
                OnPropertyChanged(nameof(CurrentMode));

                BuildView();

                _isReturningFromHelp = false;
            }
            else
            {
                CurrentMode = AppMode.ProjectList;
            }
        }
    }
}
