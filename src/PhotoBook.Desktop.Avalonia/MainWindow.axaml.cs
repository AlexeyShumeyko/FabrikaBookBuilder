using Avalonia.Controls;

namespace PhotoBook.Shell;

/// <summary>
/// The shell's window: the header and whatever screen is current behind it.
///
/// Navigation, undo and the save and export buttons come with step 3.6, and the tabs are
/// disabled until then on purpose. A tab that works before there is anything behind it
/// would be a tab that goes nowhere.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Puts a screen behind the header.
    /// </summary>
    /// <remarks>
    /// The shell decides which screen this is before the window exists, and hands over a
    /// screen already carrying its view model. The window does not resolve, does not create
    /// and does not choose - which is the whole point of the navigator sitting outside it.
    /// </remarks>
    public void ShowScreen(Control screen)
    {
        ScreenHost.Content = screen;
    }
}