using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PhotoBook.Shell.Services;

namespace PhotoBook.Shell.Views;

/// <summary>
/// The guide, in a window of its own.
/// </summary>
/// <remarks>
/// <para>
/// A window rather than a screen in the shell, because the guide is read beside the work: somebody
/// who is unsure what a group needs wants the answer and their project on the same desk, and a
/// screen in the shell replaces the project while it is open.
/// </para>
/// <para>
/// The window is decorated by the system: a page this tall is read by scrolling and by resizing,
/// and a sheet without edges would have to grow its own grip, its own close, and its own maximum
/// size on a small screen. What is inside is the design's own - see <see cref="HelpView"/>.
/// </para>
/// </remarks>
public class HelpWindow : Window
{
    public HelpWindow()
    {
        Title = "Руководство";
        // Sized against the screen, not in absolute units: the same 1060 is a comfortable page at
        // 100 % and a window wider than the screen at 150 %, where it opened full-bleed.
        if (Screens.Primary is { } screen)
        {
            Width = Math.Clamp(screen.WorkingArea.Width / screen.Scaling * 0.72, 720, 960);
            Height = Math.Clamp(screen.WorkingArea.Height / screen.Scaling * 0.82, 480, 760);
        }
        else
        {
            Width = 1060;
            Height = 800;
        }

        MinWidth = 760;
        MinHeight = 480;
        CanResize = true;
        ShowInTaskbar = false;
        // Centred on the screen, not on the window that opened it: the shell is maximised, and a
        // window this tall placed against its owner hung over the screen's edge.
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = ThemeResources.Brush("PageBackdrop");

        Content = new HelpView();

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
    }
}
