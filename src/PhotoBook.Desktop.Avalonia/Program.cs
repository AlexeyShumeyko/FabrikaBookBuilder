using System;
using Avalonia;
using Avalonia.Media;

namespace PhotoBook.Shell;

internal static class Program
{
    /// <summary>
    /// One executable, two jobs. With the probe switch it opens the step 3.0 instrument;
    /// without it, the shell. Two executables would mean two builds to keep in step for the
    /// sake of a window nobody ships. Which window opens is decided in App, from the same
    /// arguments, rather than here - this only starts the framework.
    /// </summary>
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions
            {
                // One place names the family: the view files stopped naming a font, so a
                // second typeface is this line and nowhere else.
                DefaultFamilyName = "avares://PhotoBookRenamer/Assets/Fonts#Manrope"
            })
            .LogToTrace();
}
