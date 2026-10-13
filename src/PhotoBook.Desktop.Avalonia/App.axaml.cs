using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace PhotoBook.Shell;

// The base type is spelled out. The layers below live in PhotoBook.Application, so inside a
// namespace that starts with PhotoBook the bare word "Application" resolves to that
// namespace rather than to Avalonia's class - the same trap that sat in two files of the WPF
// shell until the layers were renamed. Naming it here means the collision is written down
// once instead of rediscovered at every bare reference.
public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}