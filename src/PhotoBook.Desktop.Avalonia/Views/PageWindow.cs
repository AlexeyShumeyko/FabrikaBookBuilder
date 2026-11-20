using System;
using Avalonia.Controls;
using Avalonia.Input;
using PhotoBook.Shell.Services;

namespace PhotoBook.Shell.Views;

/// <summary>
/// A window that holds one page of the guide kind: the guide itself, the support page.
/// </summary>
/// <remarks>
/// <para>
/// Two windows share the same shell because they are the same thing - a page read beside the
/// work - and two copies of that shell would drift: one would learn to size against the desktop
/// and the other would keep its absolute width, which is exactly the bug this class exists to
/// avoid repeating.
/// </para>
/// <para>
/// The size comes from the desktop rather than from a number: the same 1060 is a comfortable
/// page at 100 % and a window as wide as the screen at 160 %, where it opens full-bleed.
/// </para>
/// </remarks>
public abstract class PageWindow : Window
{
    protected PageWindow(string title, Control page)
    {
        Title = title;

        if (Screens.Primary is { } screen)
        {
            Width = Math.Clamp(screen.WorkingArea.Width / screen.Scaling * 0.72, 720, 960);
            Height = Math.Clamp(screen.WorkingArea.Height / screen.Scaling * 0.82, 480, 760);
        }
        else
        {
            Width = 900;
            Height = 680;
        }

        MinWidth = 720;
        MinHeight = 460;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = ThemeResources.Brush("PageBackdrop");

        Content = page;

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
    }
}
