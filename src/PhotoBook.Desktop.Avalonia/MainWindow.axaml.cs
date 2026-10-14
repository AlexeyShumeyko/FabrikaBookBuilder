using Avalonia.Controls;
using Avalonia.Interactivity;
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
