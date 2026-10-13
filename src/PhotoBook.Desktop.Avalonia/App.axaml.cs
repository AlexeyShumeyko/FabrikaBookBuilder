using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

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

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The shell decides which window opens, from the arguments, rather than the
            // lifetime callback being told what to do. One decision in one place, and the
            // shell window is never constructed in probe mode - which matters, because the
            // probe is what runs while the shell is still being built.
            var probe = desktop.Args.Any(a => a.Equals("--probe", StringComparison.OrdinalIgnoreCase));

            var window = probe
                ? (Window)new ProbeWindow()
                : new MainWindow();

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}