using System;

namespace PhotoBook.Shell.Views;

/// <summary>
/// The support page: the hours and the four channels.
/// </summary>
/// <remarks>
/// <para>
/// A window of its own for now, and the last dialog of the two: the guide became a screen of
/// the shell, and this page follows it there next. Until then it must at least hand its
/// answer back instead of opening a second window.
/// </para>
/// <para>
/// So the page does not open the guide itself. It raises that the guide was asked for and
/// closes with a true, and the window that put it up is the one that shows the guide. A
/// dialog that opened another dialog left the reader two windows away from where they
/// started, and one of them belonged to a shell that was already behind them.
/// </para>
/// </remarks>
public class SupportWindow : PageWindow
{
    public SupportWindow()
        : this(new SupportView())
    {
    }

    private SupportWindow(SupportView support)
        : base("Поддержка", support)
    {
        support.GuideRequested += (_, _) => Close(true);
    }
}
