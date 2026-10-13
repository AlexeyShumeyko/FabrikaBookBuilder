using Avalonia.Controls;
using Avalonia.Markup.Xaml;

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
}