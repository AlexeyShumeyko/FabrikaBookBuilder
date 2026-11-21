using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PhotoBook.Shell.Views;

/// <summary>
/// The support page: four ways to reach a person, and the way back into the guide.
/// </summary>
/// <remarks>
/// <para>
/// A channel opens where the person already reads their messages: the browser takes Telegram,
/// the mail client takes the address, and neither is a form inside the program that would have
/// to be forwarded by hand anyway.
/// </para>
/// <para>
/// The page itself does not know how screens are put up. It says the guide was asked for and
/// leaves the navigator to decide, which keeps one place responsible for what is on screen.
/// </para>
/// </remarks>
public partial class SupportView : UserControl
{
    public SupportView()
    {
        InitializeComponent();
    }

    /// <summary>Raised when somebody asks for the guide from the bottom of the page.</summary>
    public event EventHandler? GuideRequested;

    private void OnChannelClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string address) return;

        // Only addresses that lead out of the program: a row is a link to a channel, not a way
        // to run whatever the label happens to contain.
        if (!Uri.TryCreate(address, UriKind.Absolute, out var target) ||
            (target.Scheme != Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeMailto))
        {
            ShowFailure("адрес не распознан");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nothing opened, and there is no second way to reach the same address from here.
            // The address itself is on screen, so it can be copied by hand.
            ShowFailure(ex.Message);
        }
    }

    private void OnOpenGuide(object? sender, RoutedEventArgs e) => GuideRequested?.Invoke(this, EventArgs.Empty);

    private void ShowFailure(string reason)
    {
        var note = this.FindControl<TextBlock>("FailureNote");
        if (note is null) return;

        note.Text = "Не удалось открыть ссылку. Скопируйте адрес выше — " + reason;
        note.IsVisible = true;
    }
}
