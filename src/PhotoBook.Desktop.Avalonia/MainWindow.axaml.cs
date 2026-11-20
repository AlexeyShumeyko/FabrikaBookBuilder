using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using PhotoBook.Presentation.ViewModels;
using PhotoBook.Shell.Services;
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
    private readonly SiteLoginViewModel _login;

    private bool _signInOpen;

    public MainWindow(ShellHeaderViewModel header, SiteLoginViewModel login)
    {
        InitializeComponent();
        DataContext = header;

        _login = login;

        // Bound rather than assigned once: the name arrives from the site after the window is
        // already open, and signing out has to put the invitation back.
        AccountLabel.Bind(TextBlock.TextProperty, new Binding(nameof(SiteLoginViewModel.AccountText))
        {
            Source = login
        });

        // The stored session knows the address but not the name. It is asked once, and a failure
        // leaves the address in place.
        Loaded += async (_, _) => await _login.RefreshProfileAsync();

        // The mark follows the theme that is actually in force, not the one the handler
        // would toggle to: on a machine where the choice was made once and kept, this is
        // the only place the icon can learn it.
        Loaded += (_, _) => ShowThemeGlyph(ActualThemeVariant);

        // A refused token opens the same window a fresh start would, once: the profile read and
        // the header button can both find the session gone in the same moment.
        _login.SessionExpired += async (_, _) => await SignInAsync();
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
    /// Switches between the light and the dark theme, and remembers the choice.
    /// </summary>
    /// <remarks>
    /// The variant is changed on the application rather than on this window, so the dialogs
    /// and the sheet layer behind it change with it; a window that switched alone would leave
    /// every dialog it opened in the other theme. Saving is the second half of the job: a
    /// switch that is forgotten at the next launch is a switch that did not work.
    /// </remarks>
    private void OnThemeClick(object? sender, RoutedEventArgs e)
    {
        var next = ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;

        // Spelled out in full: inside a namespace starting with PhotoBook the bare word
        // Application resolves to that namespace, not to Avalonia's class.
        if (Avalonia.Application.Current is { } application)
            application.RequestedThemeVariant = next;

        ThemePreference.Save(next);
        ShowThemeGlyph(next);
    }

    /// <summary>
    /// Draws the switch as the way out of the theme on screen: a sun in the light theme, a
    /// moon in the dark one.
    /// </summary>
    private void ShowThemeGlyph(ThemeVariant variant) =>
        ThemeGlyph.Text = variant == ThemeVariant.Dark ? "\uE708" : "\uE706";

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

    /// <summary>
    /// Opens the support page: the hours and the four channels.
    /// </summary>
    /// <remarks>
    /// A window of its own beside the guide, because the two answer different questions and
    /// somebody looking for an address should not have to read seven chapters to find it.
    /// </remarks>
    private async void OnContactsClick(object? sender, RoutedEventArgs e)
        => await new SupportWindow().ShowDialog(this);

    /// <summary>
    /// Opens the account when somebody is signed in, and the sign-in form when nobody is.
    /// </summary>
    /// <remarks>
    /// One place in the header for both states, so the customer does not have to find a menu to
    /// leave an account. The label follows the session by binding; nothing is copied back here.
    /// </remarks>
    private async void OnAccountClick(object? sender, RoutedEventArgs e)
    {
        if (_login.IsSignedIn)
            await new AccountWindow(_login).ShowDialog(this);
        else
            await SignInAsync();
    }

    /// <summary>
    /// Shows the sign-in window once, whoever asked for it.
    /// </summary>
    /// <remarks>
    /// Two events can arrive together - the header pressed and the profile refused - and two
    /// sign-in windows on top of each other would ask for the same password twice.
    /// </remarks>
    public async Task SignInAsync()
    {
        if (_signInOpen)
            return;

        _signInOpen = true;
        try
        {
            await new LoginWindow(_login).ShowDialog(this);
        }
        finally
        {
            _signInOpen = false;
        }
    }

    /// <summary>
    /// Puts a panel over the page, dims the page, and waits for the panel's answer.
    /// </summary>
    /// <remarks>
    /// One at a time, and the layer goes down again whichever way it ends - an answer, a
    /// refusal, or an exception thrown out of the panel. A layer left up over a dead panel
    /// would dim the page for the rest of the session with nothing on it to explain why.
    /// </remarks>
    public async Task<T?> ShowSheetAsync<T>(OverlaySheet<T> sheet) where T : class
    {
        SheetHost.Content = sheet;
        SheetLayer.IsVisible = true;

        try
        {
            return await sheet.Answer;
        }
        finally
        {
            SheetLayer.IsVisible = false;
            SheetHost.Content = null;
        }
    }
}
