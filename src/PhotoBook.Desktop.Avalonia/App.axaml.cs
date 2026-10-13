using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PhotoBook.Application;
using PhotoBook.Infrastructure;
using PhotoBook.Presentation;
using PhotoBook.Presentation.ViewModels;
using PhotoBook.Shell.Converters;
using PhotoBook.Shell.Navigation;
using PhotoBook.Shell.Services;
using PhotoBook.Shell.Services;
using PhotoBook.Shell.Views;

namespace PhotoBook.Shell;

public partial class App : Avalonia.Application
{
    // The base type is spelled out. The layers below live in PhotoBook.Application, so inside
    // a namespace that starts with PhotoBook the bare word "Application" would resolve to
    // that namespace rather than to Avalonia's class. The shell namespace deliberately does
    // not end in Avalonia for the same reason: inside one, the bare word "Avalonia" would
    // resolve to its own last segment instead of the framework's root.
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private ServiceProvider? _services;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The shell decides which window opens, from the arguments, rather than the
            // lifetime callback being told what to do. One decision in one place, and the
            // shell window is never constructed in probe mode - which matters, because the
            // probe is what runs while the shell is still being built.
            var probe = desktop.Args.Any(a => a.Equals("--probe", StringComparison.OrdinalIgnoreCase));

            if (probe)
            {
                desktop.MainWindow = new ProbeWindow();
                base.OnFrameworkInitializationCompleted();
                return;
            }

            var services = new ServiceCollection();
            ConfigureServices(services);
            _services = services.BuildServiceProvider();

            FilePathToBitmapConverter.Attach(_services);

            var navigator = _services.GetRequiredService<AvaloniaNavigator>();

            var window = new MainWindow();
            window.ShowScreen(new ProjectListView
            {
                DataContext = _services.GetRequiredService<ProjectListViewModel>()
            });

            desktop.MainWindow = window;

            // Disposal hangs off the lifetime's own shutdown event. There is no OnExit to
            // override here: the classic desktop lifetime has no such callback with an event
            // argument - the names look obvious and none of them exist.
            desktop.ShutdownRequested += (_, _) => _services?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The same shape as the WPF shell's, and deliberately so: same layers, same lifetimes,
    /// different implementations of the ports. A difference here would be a second program
    /// wearing the same name.
    /// </summary>
    private void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging();

        services.AddPhotoBookApplication();
        services.AddPhotoBookInfrastructure();
        services.AddPhotoBookPresentation();

        // This shell's own answers to the ports. Everything above this line is shared.
        services.AddSingleton<IProjectDialogs, AvaloniaProjectDialogs>();
        services.AddSingleton<AvaloniaNavigator>();
        services.AddSingleton<IShellNavigator>(sp => sp.GetRequiredService<AvaloniaNavigator>());
        services.AddSingleton<IUiThread, AvaloniaUiThread>();

        // Registered as its own type as well as the port, because the card bindings need to
        // reach a decoded image and the port deliberately does not hand one back.
        services.AddSingleton<AvaloniaImageCache>();
        services.AddSingleton<IDecodedImageCache>(sp => sp.GetRequiredService<AvaloniaImageCache>());

        // The project list's view model is not a singleton: it is built per visit, because it
        // exists to show the projects as they are at the moment it is opened. The navigator
        // creates it, which is why it is registered as transient rather than resolved here.
        services.AddTransient<ProjectListViewModel>();
    }
}

