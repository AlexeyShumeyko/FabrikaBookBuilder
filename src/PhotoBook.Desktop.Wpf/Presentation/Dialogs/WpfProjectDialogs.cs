using System.Threading.Tasks;
using PhotoBook.Application;

namespace PhotoBookRenamer.Presentation.Dialogs
{
    /// <summary>
    /// The shell's answer to <see cref="IProjectDialogs"/>: the same three windows, reached
    /// through the container instead of through a static call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AppDialogs</c> stays exactly as it is. Creating a window in WPF is a static act -
    /// there is no instance to hold - so a class that opens windows has nowhere to take a
    /// constructor argument, and making it implement a port is not possible at all.
    /// </para>
    /// <para>
    /// This adapter is the seam. What depends on the port - the view models - can be handed
    /// it by the container and be tested with a stand-in; what does the work stays a window
    /// factory that only this shell can run. The Avalonia shell has its own implementation of
    /// the same three cases and does not touch this file.
    /// </para>
    /// <para>
    /// The methods return an already-completed task, and that is not a shortcut: WPF's
    /// <c>ShowDialog</c> blocks the caller inside its own message loop, so by the time these
    /// return the answer exists. Nothing is deferred, nothing is yielded, and a caller that
    /// awaits behaves exactly as it did when the port was synchronous.
    /// </para>
    /// </remarks>
    internal class WpfProjectDialogs : IProjectDialogs
    {
        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string confirmText = "Подтвердить",
            bool destructive = true)
            => Task.FromResult(AppDialogs.Confirm(title, message, confirmText, destructive));

        public Task NotifyAsync(
            string title,
            string message,
            string okText = "Понятно",
            bool destructive = false)
        {
            AppDialogs.Notify(title, message, okText, destructive);
            return Task.CompletedTask;
        }

        public Task<ConfirmOutcome> AskAsync(
            string title,
            string message,
            string primaryText,
            string secondaryText)
            => Task.FromResult(AppDialogs.Ask(title, message, primaryText, secondaryText));
    }
}
