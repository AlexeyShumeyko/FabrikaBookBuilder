using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PhotoBook.Application;
using PhotoBook.Core;
using PhotoBook.Infrastructure;
using PhotoBook.Presentation;
using PhotoBook.Presentation.ViewModels;
using PhotoBook.Shell.Converters;
using PhotoBook.Shell.Navigation;
using PhotoBook.Shell.Platform;
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

            // The navigator drives the window: it is the thing that knows which screen a mode
            // means, so the window only has to hold what it is given. Attaching is also what
            // puts the first screen up, so nothing is shown beforehand.
            navigator.Attach(window.ScreenHost);

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
        // The journal first, before anything can fail. This shell is a windowed application
        // with no console, so without this every warning it writes - a file that will not
        // decode, an index that could not be saved - goes nowhere at all. The WPF shell
        // installs it for the same reason, and the second shell quietly not doing it is
        // exactly the kind of difference between shells that nobody notices.
        Diagnostics.InstallJournal();

        services.AddLogging();

        services.AddPhotoBookApplication();
        services.AddPhotoBookInfrastructure();
        services.AddPhotoBookPresentation();

        // This shell's own answers to the ports. Everything above this line is shared.
        services.AddSingleton<IProjectDialogs, AvaloniaProjectDialogs>();
        services.AddSingleton<IUiThread, AvaloniaUiThread>();
        services.AddSingleton<IPickFiles, AvaloniaPickFiles>();

        // The windows that collect an answer - export options, folder picker, page number -
        // are step 3.5. Until then this stands in, so the editors can be constructed at all,
        // and it fails loudly rather than pretending a cancelled export is a real one.
        services.AddSingleton<IEditorDialogs, DialogsNotPortedYet>();

        // Registered as its own type as well as the port, because the card bindings need to
        // reach a decoded image and the port deliberately does not hand one back.
        services.AddSingleton<AvaloniaImageCache>();
        services.AddSingleton<IDecodedImageCache>(sp => sp.GetRequiredService<AvaloniaImageCache>());

        // Both of these are registered through factories rather than by type, because each
        // one needs a Func<ProjectListViewModel>: the projects list is built per visit, and
        // the container cannot construct a delegate on its own.
        services.AddSingleton<IScreenViewFactory>(sp => new AvaloniaScreenViewFactory(
            sp.GetRequiredService<UniqueFoldersViewModel>(),
            sp.GetRequiredService<CombinedModeViewModel>(),
            () => sp.GetRequiredService<ProjectListViewModel>()));

        services.AddSingleton(sp => new AvaloniaNavigator(
            sp.GetRequiredService<IScreenViewFactory>(),
            sp.GetRequiredService<UniqueFoldersViewModel>(),
            sp.GetRequiredService<CombinedModeViewModel>(),
            () => sp.GetRequiredService<ProjectListViewModel>()));

        services.AddSingleton<IShellNavigator>(sp => sp.GetRequiredService<AvaloniaNavigator>());

        // The projects list is not a singleton: it is built per visit, because it exists to
        // show the projects as they are at the moment it is opened. The navigator creates it.
        services.AddTransient<ProjectListViewModel>();
    }
}