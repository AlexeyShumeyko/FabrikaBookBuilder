using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoBook.Core;
using PhotoBookRenamer.Application;
using PhotoBookRenamer.Presentation.Views;

namespace PhotoBookRenamer.Presentation.ViewModels
{
    /// <summary>State, commands, and how this screen reaches the shell.</summary>
    public partial class ProjectListViewModel : ViewModelBase
    {
        private readonly IProjectListService _projectListService;
        private readonly IProjectRepository _projects;
        private readonly IShellNavigator _navigator;
        private bool _isLoading;
        private string? _errorMessage;
        private ProjectInfo? _selectedProject;

        public ProjectListViewModel(
            IProjectListService projectListService,
            IProjectRepository projects,
            IShellNavigator navigator)
        {
            _projectListService = projectListService;
            _projects = projects;
            _navigator = navigator;
            Projects = new ObservableCollection<ProjectInfo>();

            LoadProjectsCommand = new AsyncRelayCommand(LoadProjectsAsync);
            CreateProjectCommand = new RelayCommand(CreateProject);
            OpenProjectCommand = new RelayCommand<ProjectInfo>(OpenProject);
            DeleteProjectCommand = new AsyncRelayCommand<ProjectInfo>(DeleteProjectAsync, CanDeleteProject);

            // CollectionChanged -> ProjectsCount, so "N проектов" and the empty state
            // re-evaluate. See the remark on ProjectsCount.
            Projects.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ProjectsCount));
            _ = LoadProjectsAsync();
        }

        public ObservableCollection<ProjectInfo> Projects { get; }

        /// <summary>
        /// Count for bindings that must react to items being added.
        ///
        /// WPF does not re-evaluate a binding just because an ObservableCollection grew:
        /// it only listens for INotifyPropertyChanged on the binding source. Binding
        /// straight to <c>Projects</c> therefore latched onto whatever the collection
        /// contained at load time (empty), and the "no projects yet" panel stayed on top
        /// of the freshly loaded cards. Subscribing to CollectionChanged and re-raising is
        /// what makes such bindings update.
        /// </summary>
        public int ProjectsCount => Projects.Count;

        public ProjectInfo? SelectedProject
        {
            get => _selectedProject;
            set
            {
                SetProperty(ref _selectedProject, value);
                ((AsyncRelayCommand<ProjectInfo>)DeleteProjectCommand).NotifyCanExecuteChanged();
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public string ModeTitle => "Все проекты";

        public ICommand LoadProjectsCommand { get; }
        public ICommand CreateProjectCommand { get; }
        public ICommand OpenProjectCommand { get; }
        public ICommand DeleteProjectCommand { get; }
        public ICommand BackCommand { get; }

        private AppMode CurrentMode
        {
            set
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    // Flipping the shell's mode is enough: the shell is the MainWindow's
                    // DataContext, and re-assigning that used to rebuild the persistent
                    // header on every visit.
                    _navigator.CurrentMode = value;
                });
            }
        }

        /// <summary>
        /// Opening a saved project is one of the two ways into an editor. The mode tab is
        /// disabled otherwise, so this has to go through OpenProject rather than a plain
        /// CurrentMode change - the setter would refuse it.
        /// </summary>
        private void OpenProjectMode(AppMode mode)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _navigator.OpenProject(mode);
            });
        }

    }
}
