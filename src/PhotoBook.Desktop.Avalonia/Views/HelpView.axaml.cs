using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PhotoBook.Infrastructure;

namespace PhotoBook.Shell.Views;

/// <summary>
/// The guide's page: the cover, the spine and the chapters.
/// </summary>
/// <remarks>
/// <para>
/// The chapters are a scroll of panels, not a set of tabs: the whole point of a manual is to be
/// read in order and to be findable by scrolling back, and a tab per chapter turns one page into
/// seven that each have to be looked at separately.
/// </para>
/// <para>
/// The spine scrolls the chapter's own panel to the top of the view. The offset comes from the
/// panel's position inside the chapters' stack, which is the one number that stays true when the
/// text above it grows or is edited.
/// </para>
/// </remarks>
public partial class HelpView : UserControl
{
    public HelpView()
    {
        InitializeComponent();

        JournalPath.Text = Diagnostics.JournalPath;
    }

    private void OnSpineClick(object? sender, RoutedEventArgs e)
    {
        var number = (sender as Button)?.Tag as string;
        var chapter = Chapters.Children
            .OfType<Control>()
            .FirstOrDefault(control => control.Name == "Chapter" + number);

        ChapterScroll.Offset = new Avalonia.Vector(0, chapter is null ? 0 : chapter.Bounds.Top);
    }

    private void OnOpenJournalFolder(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Path.GetDirectoryName(Diagnostics.JournalPath);
            if (string.IsNullOrEmpty(folder)) return;

            Directory.CreateDirectory(folder);

            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // The path is on screen right above the button: nothing to explain that it does not
            // already say, and a dialog about a folder would be louder than the problem.
        }
    }
}
