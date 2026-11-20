using System;

namespace PhotoBook.Shell.Views;

/// <summary>
/// The guide, in a window of its own.
/// </summary>
/// <remarks>
/// A window rather than a screen in the shell, because the guide is read beside the work: somebody
/// who is unsure what a group needs wants the answer and their project on the same desk, and a
/// screen in the shell replaces the project while it is open. What is inside is the design's own -
/// see <see cref="HelpView"/>; the shell itself is <see cref="PageWindow"/>.
/// </remarks>
public class HelpWindow : PageWindow
{
    public HelpWindow()
        : this(new HelpView())
    {
    }

    private HelpWindow(HelpView guide)
        : base("Руководство", guide)
    {
        // The last chapter ends by offering to write to a person, and the support page is where
        // that happens: it opens over the guide and the reader comes back where they left off.
        guide.SupportRequested += (_, _) => _ = new SupportWindow().ShowDialog(this);
    }
}

/// <summary>
/// The support page: the hours and the four channels.
/// </summary>
/// <remarks>
/// Its own window beside the guide, because the two answer different questions: somebody who
/// needs an address should not have to read seven chapters to find it. The page offers the way
/// back into the guide, and that opens over this window as well.
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
        support.GuideRequested += (_, _) => _ = new HelpWindow().ShowDialog(this);
    }
}
