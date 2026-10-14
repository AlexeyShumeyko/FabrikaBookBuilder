using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
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
using PhotoBook.Shell.ViewModels;

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

        // Set here rather than on the Application element in the markup. A value written on
        // the root element is applied while the markup is being read, which is after the
        // platform theme has already chosen a variant - so it arrives too late and silently
        // does nothing. Set right after loading, it takes. The symptom of getting this wrong
        // is the platform's own control colours arriving dark while the product's palette
        // stays light, which reads as one stray dark tab and is not one stray dark tab.
        RequestedThemeVariant = ThemeVariant.Light;
    }

    /// <summary>
    /// Looks for a newer version once the window is up, and says so if there is one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A failed check is not worth interrupting anyone for: it is logged and forgotten. The
    /// person who started the program is working, not waiting to be told that the internet
    /// is having a moment.
    /// </para>
    /// <para>
    /// It runs on the UI thread's scheduler rather than on a pool thread, because showing a
    /// window is a UI-thread job and asking for one from a background thread is the deadlock
    /// this wave has already met once.
    /// </para>
    /// </remarks>
    private static async Task CheckForUpdatesLaterAsync(IServiceProvider services)
    {
        try
        {
            await Task.Delay(2000);

            var feed = services.GetRequiredService<IUpdateFeed>();
            var update = await feed.CheckForUpdatesAsync();
            if (!update.IsSuccess || !update.Value) return;

            var latest = await feed.GetLatestVersionAsync();
            if (string.IsNullOrEmpty(latest)) return;

            var notes = await feed.GetLatestReleaseNotesAsync();

            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var window = new UpdateWindow(new UpdateViewModel(feed, latest, notes));
                var owner = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (owner is not null) await window.ShowDialog(owner);
                else await window.ShowDialog(window);
            });
        }
        catch (Exception ex)
        {
            // Same rule as everywhere else in the shell: a failure of a background nicety is
            // written down, not shown.
            services.GetRequiredService<IJournal>().Write(
                JournalLevel.Warning, "update", "Проверка обновлений не завершилась", ex);
        }
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
            PageToBitmapConverter.Attach(_services);

            var navigator = _services.GetRequiredService<AvaloniaNavigator>();
            var window = new MainWindow(_services.GetRequiredService<ShellHeaderViewModel>());

            // The screen to open is decided before the window exists, which is what makes it
            // reliable. Done from a command it would be an asynchronous call whose
            // continuations need a running event loop, and at this point there is no loop
            // yet - four attempts at that ended with the switch quietly doing nothing. Done
            // here it is plain sequential code before the framework starts pumping.
            var open = ReadOption(desktop.Args, "--open");
            if (!string.IsNullOrEmpty(open))
            {
                // The mode-select screen has no project to open, so it is a mode and nothing
                // else. Handled here rather than inside OpenNewest, which exists to find a
                // project on disk.
                if (open.Equals("start", StringComparison.OrdinalIgnoreCase))
                    navigator.CurrentMode = AppMode.StartScreen;
                else
                    OpenNewest(_services, navigator, open);
            }

            // The navigator drives the window: it is the thing that knows which screen a mode
            // means, so the window only has to hold what it is given. Attaching is also what
            // puts the current screen up.
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
        services.AddSingleton<IEditorDialogs, AvaloniaEditorDialogs>();

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

        // The header asks the navigator where the shell is rather than holding a copy of it,
        // so a tab can never disagree with the screen behind it.
        services.AddSingleton<ShellHeaderViewModel>();

        // The projects list is not a singleton: it is built per visit, because it exists to
        // show the projects as they are at the moment it is opened. The navigator creates it.
        services.AddTransient<ProjectListViewModel>();

        // The update check starts once the window is up, not before it: the person who
        // started the program should see the program first, and a window that appears two
        // seconds late is a window that looks broken on a slow connection.
        _ = CheckForUpdatesLaterAsync(_services);
    }

    /// <summary>
    /// Reads a switch that may arrive as "--name value" or as "--name=value".
    /// </summary>
    /// <remarks>
    /// A launcher passes them as two arguments and a shell passes one. Reading only the
    /// second form gives a switch that appears to do nothing at all - which is what happened,
    /// four times over, for a different reason each time.
    /// </remarks>
    private static string? ReadOption(IReadOnlyList<string> args, string name)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                return args[i + 1];

            var prefix = name + "=";
            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return args[i][prefix.Length..].Trim();
        }

        return null;
    }

    /// <summary>
    /// Opens the most recently changed project of the named mode, before the window exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It goes through the navigator rather than through the projects list's command, and that
    /// is the whole point. The command is asynchronous and its continuation needs a running
    /// event loop; before the framework starts pumping there is none, so the command starts,
    /// finds no loop, and the screen never changes. The navigator's contract is synchronous by
    /// design - it is handed a project and told which mode wants it - so the sequence below is
    /// plain sequential code with nothing to wait for.
    /// </para>
    /// <para>
    /// It exists so an editor screen can be photographed. Verifying them the honest way needs
    /// a way to drive the shell's UI, and that is step 3.9 tooling.
    /// </para>
    /// </remarks>
    private static void OpenNewest(IServiceProvider services, AvaloniaNavigator navigator, string mode)
    {
        var wanted = mode.Equals("combined", StringComparison.OrdinalIgnoreCase)
            ? AppMode.Combined
            : AppMode.UniqueFolders;

        // On the thread pool, and waited for from here.
        //
        // Reading these two files inline and waiting for them deadlocks: the UI thread is the
        // one waiting, no dispatcher is pumping yet because the window has not been shown,
        // and the continuation that would finish the read has nowhere to resume. The process
        // stays alive with no window - which is what it did for an afternoon. On the pool
        // there is no captured context and so nothing to wait for; only the hand-over comes
        // back to the UI thread.
        var opened = Task.Run(() =>
        {
            var record = services.GetRequiredService<IProjectListService>()
                .GetAllProjectsAsync().GetAwaiter().GetResult()
                .Where(p => p.Mode == wanted)
                .OrderByDescending(p => p.LastModified)
                .FirstOrDefault();

            if (record?.FilePath is not { } path || !File.Exists(path))
                return (project: (Project?)null, record: (ProjectInfo?)null);

            var project = services.GetRequiredService<IProjectRepository>()
                .LoadAsync(path).GetAwaiter().GetResult();

            return (project: project, record: record);
        }).GetAwaiter().GetResult();

        if (opened.project is null || opened.record is null) return;

        navigator.HandProjectToEditor(opened.project, opened.record);
        navigator.CurrentMode = wanted;
    }
}

