using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoBookRenamer.Models;

namespace PhotoBookRenamer.ViewModels
{
    public class HelpViewModel : ViewModelBase
    {
        private HelpSection _currentSection;

        public HelpViewModel(HelpSection? initialSection = null)
        {
            CurrentSection = initialSection ?? HelpSection.Overview;
            NavigateToSectionCommand = new RelayCommand<HelpSection>(NavigateToSection);
            BackCommand = new RelayCommand(Back);
        }

        public HelpSection CurrentSection
        {
            get => _currentSection;
            set => SetProperty(ref _currentSection, value);
        }

        public ICommand NavigateToSectionCommand { get; }
        public ICommand BackCommand { get; }

        private void NavigateToSection(HelpSection section)
        {
            CurrentSection = section;
        }

        private void Back()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (System.Windows.Application.Current.MainWindow is Views.MainWindow mainWindow)
                {
                    var serviceProvider = ((App)System.Windows.Application.Current).GetServiceProvider();
                    if (serviceProvider != null)
                    {
                        var mainVm = serviceProvider.GetRequiredService<MainViewModel>();
                        // Возвращаемся к предыдущему режиму
                        mainVm.ReturnFromHelp();
                        // Убеждаемся, что DataContext обновлен
                        if (mainWindow.DataContext != mainVm)
                        {
                            mainWindow.DataContext = mainVm;
                        }
                        // Принудительно обновляем UI
                        mainWindow.UpdateLayout();
                    }
                }
            });
        }
    }
}

