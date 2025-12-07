using System;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoBookRenamer.Models;
using PhotoBookRenamer.Services;
using PhotoBookRenamer.Views;

namespace PhotoBookRenamer.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private AppMode _currentMode = AppMode.StartScreen;
        private AppMode? _previousModeBeforeHelp;
        private object? _currentView;
        private bool _isReturningFromHelp = false;
        public HelpSection? HelpSection { get; private set; }

        public MainViewModel()
        {
            SwitchToUniqueFoldersCommand = new RelayCommand(() => CurrentMode = AppMode.StartScreen);
            SwitchToCombinedModeCommand = new RelayCommand(() => CurrentMode = AppMode.StartScreen);
            
            // По умолчанию показываем список всех проектов
            Application.Current.Dispatcher.Invoke(() =>
            {
                var serviceProvider = ((App)Application.Current).GetServiceProvider();
                if (serviceProvider != null)
                {
                    var projectListService = serviceProvider.GetRequiredService<IProjectListService>();
                    var projectListVm = new ProjectListViewModel(projectListService);
                    CurrentView = new ProjectListView(projectListVm);
                    _currentMode = AppMode.ProjectList;
                }
            });
        }

        public AppMode CurrentMode
        {
            get => _currentMode;
            set
            {
                if (SetProperty(ref _currentMode, value))
                {
                    // Если мы возвращаемся из помощи, не создаем новый View
                    if (_isReturningFromHelp)
                    {
                        _isReturningFromHelp = false;
                        return;
                    }
                    
                    // Обновляем представление при изменении режима
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var serviceProvider = ((App)Application.Current).GetServiceProvider();
                        if (serviceProvider != null)
                        {
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
                                    // Используем существующий ViewModel из сервиса, чтобы сохранить состояние
                                    var uniqueVm = serviceProvider.GetRequiredService<UniqueFoldersViewModel>();
                                    // Создаем новый View, но используем существующий ViewModel (Singleton), который сохраняет состояние
                                    CurrentView = new UniqueFoldersView(uniqueVm);
                                    break;
                                case AppMode.Combined:
                                    // Используем существующий ViewModel из сервиса, чтобы сохранить состояние
                                    var combinedVm = serviceProvider.GetRequiredService<CombinedModeViewModel>();
                                    // Создаем новый View, но используем существующий ViewModel (Singleton), который сохраняет состояние
                                    CurrentView = new CombinedModeView(combinedVm);
                                    break;
                                case AppMode.Help:
                                    var helpSection = HelpSection ?? Models.HelpSection.Overview;
                                    var helpVm = new HelpViewModel(helpSection);
                                    CurrentView = new HelpView(helpVm);
                                    break;
                            }
                        }
                    });
                }
            }
        }

        public object? CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }

        public ICommand SwitchToUniqueFoldersCommand { get; }
        public ICommand SwitchToCombinedModeCommand { get; }
        
        public void OpenHelp(Models.HelpSection? section = null)
        {
            // Сохраняем только текущий режим перед переходом в помощь
            // ViewModel сохраняет состояние, так как он Singleton
            _previousModeBeforeHelp = _currentMode;
            HelpSection = section;
            CurrentMode = AppMode.Help;
        }
        
        public void ReturnFromHelp()
        {
            // Возвращаемся к предыдущему режиму
            // ViewModel сохраняет состояние (Singleton), поэтому при создании нового View
            // с тем же ViewModel все данные будут на месте
            if (_previousModeBeforeHelp.HasValue)
            {
                // Устанавливаем флаг, чтобы не создавать новый View в setter CurrentMode
                // Вместо этого создадим View вручную с существующим ViewModel
                _isReturningFromHelp = true;
                var previousMode = _previousModeBeforeHelp.Value;
                _previousModeBeforeHelp = null;
                
                // Восстанавливаем режим (setter не создаст View из-за флага)
                _currentMode = previousMode;
                OnPropertyChanged(nameof(CurrentMode));
                
                // Создаем View вручную с существующим ViewModel (Singleton сохраняет состояние)
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var serviceProvider = ((App)Application.Current).GetServiceProvider();
                    if (serviceProvider != null)
                    {
                        switch (previousMode)
                        {
                            case AppMode.UniqueFolders:
                                var uniqueVm = serviceProvider.GetRequiredService<UniqueFoldersViewModel>();
                                // ViewModel сохраняет состояние (Singleton), поэтому при создании нового View
                                // все данные будут отображены автоматически через привязки данных
                                CurrentView = new UniqueFoldersView(uniqueVm);
                                break;
                            case AppMode.Combined:
                                var combinedVm = serviceProvider.GetRequiredService<CombinedModeViewModel>();
                                // ViewModel сохраняет состояние (Singleton), поэтому при создании нового View
                                // все данные будут отображены автоматически через привязки данных
                                CurrentView = new CombinedModeView(combinedVm);
                                break;
                            case AppMode.ProjectList:
                                var projectListService = serviceProvider.GetRequiredService<IProjectListService>();
                                var projectListVm = new ProjectListViewModel(projectListService);
                                CurrentView = new ProjectListView(projectListVm);
                                projectListVm.LoadProjectsCommand.Execute(null);
                                break;
                            case AppMode.StartScreen:
                                CurrentView = new StartScreenView(this);
                                break;
                        }
                    }
                });
                
                _isReturningFromHelp = false;
            }
            else
            {
                // Если ничего не сохранено, возвращаемся к списку проектов
                CurrentMode = AppMode.ProjectList;
            }
        }
    }
}

