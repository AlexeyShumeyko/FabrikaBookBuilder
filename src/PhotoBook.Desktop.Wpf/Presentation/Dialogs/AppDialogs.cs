using System.Windows;

namespace PhotoBookRenamer.Presentation.Dialogs
{
    /// <summary>
    /// One way for the ViewModels to ask the user something.
    ///
    /// ViewModels used to call <c>System.Windows.MessageBox</c> directly, which meant the
    /// system's own grey box could appear in the middle of this app at any moment. These
    /// helpers keep the look in one place and give every caller the same owner window, so
    /// the dialog is centred on the app rather than on the screen.
    ///
    /// They are deliberately static and side-effect free apart from showing a window: a
    /// ViewModel must not have to know about owner windows, and the owner explicitly did not
    /// want to "break anything else" to get there.
    /// </summary>
    public static class AppDialogs
    {
        /// <summary>
        /// Asks a yes/no question. Returns true only for the confirm button - closing the
        /// window with Escape or the X counts as "no", as it must for a destructive action.
        /// </summary>
        public static bool Confirm(
            string title,
            string message,
            string confirmText = "Подтвердить",
            bool destructive = true)
        {
            var dialog = new ConfirmDialog();
            dialog.Configure(title, message, confirmText, destructive);
            var owner = System.Windows.Application.Current?.MainWindow;
            if (owner != null && !ReferenceEquals(owner, dialog))
                dialog.Owner = owner;

            return dialog.ShowConfirmed();
        }

        /// <summary>
        /// Says something the user has to read and acknowledge. Same chrome, one button, so
        /// a warning in this app no longer looks like a different program talking.
        /// </summary>
        public static void Notify(
            string title,
            string message,
            string okText = "Понятно",
            bool destructive = false)
        {
            var dialog = new ConfirmDialog();
            dialog.Configure(title, message, okText, destructive);
            dialog.ShowSingleButton();

            var owner = System.Windows.Application.Current?.MainWindow;
            if (owner != null && !ReferenceEquals(owner, dialog))
                dialog.Owner = owner;

            dialog.ShowDialog();
        }

        /// <summary>
        /// A question with three real answers - the only kind in the app is "the project is
        /// not saved: save, discard, or stay", where a two-button box would have to throw
        /// work away or trap the user. The middle button is the non-primary, non-destructive
        /// answer; the right-hand one stays the coloured default.
        /// </summary>
        public static ConfirmOutcome Ask(
            string title,
            string message,
            string primaryText,
            string secondaryText)
        {
            var dialog = new ConfirmDialog();
            dialog.Configure(title, message, primaryText, destructive: true, secondaryText: secondaryText);

            var owner = System.Windows.Application.Current?.MainWindow;
            if (owner != null && !ReferenceEquals(owner, dialog))
                dialog.Owner = owner;

            return dialog.ShowOutcome();
        }
    }
}
