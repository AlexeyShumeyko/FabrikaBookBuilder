using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PhotoBook.Shell.ViewModels;
using PhotoBook.Shell.Views;

namespace PhotoBook.Shell;

/// <summary>
/// The shell's window: the header and whatever screen is current behind it.
/// </summary>
/// <remarks>
/// It is handed the header's view model and nothing else. It does not resolve anything, does
/// not choose a screen and does not know what a mode means - all of that is the navigator's,
/// which is why the window can be this small.
/// </remarks>
public partial class MainWindow : Window
{
    public MainWindow(ShellHeaderViewModel header)
    {
        InitializeComponent();
        DataContext = header;
    }

    /// <summary>
    /// A click anywhere that is not a text box or a button applies a name that is being
    /// renamed.
    /// </summary>
    /// <remarks>
    /// The box losing the focus is not enough on its own, and that is the whole reason this
    /// is on the window. Clicking a label takes no focus, because a label is not something
    /// that can hold focus - so nothing ever loses it, and the name stays half-typed with
    /// no sign it was not saved. The other shell puts the same rule on its window, and it
    /// defers it for the same reason: the click that dismisses the box is the very event
    /// being handled here, and the screen asks each of its own to do the work.
    ///
    /// The walk stops at the first text box or button on the way up. A click on either is
    /// either typing or a press, and the rename is none of that control's business.
    /// </remarks>
    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        for (Visual? node = e.Source as Visual; node is not null; node = node.GetVisualParent())
        {
            if (node is TextBox or Button) return;
        }

        foreach (var open in this.GetVisualDescendants().OfType<IPendingRename>())
        {
            open.TryCommitPendingRename();
        }
    }

    /// <summary>
    /// Opens the help window against this one.
    /// </summary>
    /// <remarks>
    /// The other two buttons in the header stay stubs, and their tooltips say so rather than
    /// letting anyone file a report about a button that does nothing. Help is the one that
    /// has something to say today: where the journal is, which is the only thing that turns
    /// "it did not work" into something that can be looked at.
    /// </remarks>
    private async void OnHelpClick(object? sender, RoutedEventArgs e)
        => await new HelpWindow().ShowDialog(this);
}
