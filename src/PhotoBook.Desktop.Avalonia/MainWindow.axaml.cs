using Avalonia.Controls;
using PhotoBook.Shell.ViewModels;

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
}
