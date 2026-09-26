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
            GoToUniqueFoldersCommand = new RelayCommand(() => CurrentMode = AppMode.UniqueFolders);
            GoToCombinedModeCommand = new RelayCommand(() => CurrentMode = AppMode.Combined);

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
                if (!SetProperty(ref _currentMode, value)) return;

                // Если мы возвращаемся из помощи, не создаем новый View
                if (_isReturningFromHelp)
                {
                    _isReturningFromHelp = false;
                    return;
                }

                BuildView();
            }
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
