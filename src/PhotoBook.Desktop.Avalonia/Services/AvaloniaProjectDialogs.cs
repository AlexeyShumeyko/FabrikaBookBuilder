using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using PhotoBook.Application;
using PhotoBook.Shell.Views;

namespace PhotoBook.Shell.Services
{
    /// <summary>
    /// This shell's answer to <see cref="IProjectDialogs"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project list uses exactly one of the three: the question asked before a project is
    /// deleted. The other two are here because the contract is not optional, and an
    /// unimplemented member on a public interface is a runtime failure rather than a
    /// compile-time one.
    /// </para>
    /// <para>
    /// Nothing here waits on the result. Avalonia's <c>ShowDialog</c> returns a task and does
    /// not block, which is the whole reason the port is asynchronous; blocking on it from the
    /// UI thread would freeze the window with the dialog already closed.
    /// </para>
    /// </remarks>
    public class AvaloniaProjectDialogs : IProjectDialogs
    {
        public async Task<bool> ConfirmAsync(
            string title,
            string message,
            string confirmText = "Подтвердить",
            bool destructive = true)
        {
            var window = new ConfirmWindow(confirmText, message, destructive)
            {
                Title = title
            };

            var owner = Owner();
            if (owner != null) window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            return await window.ShowDialog<bool>(owner);
        }

        public async Task NotifyAsync(
            string title,
            string message,
            string okText = "Понятно",
            bool destructive = false)
        {
            var window = new NotifyWindow(okText, message, destructive)
            {
                Title = title
            };

            var owner = Owner();
            if (owner != null) window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            await window.ShowDialog(owner);
        }

        public async Task<ConfirmOutcome> AskAsync(
            string title,
            string message,
            string primaryText,
            string secondaryText)
        {
            // The three-way question belongs to the editors, which are not ported yet. The
            // primary answer is the one that cannot be got wrong by accident, so that is what
            // it does - and it is stated here rather than hidden behind a default.
            var window = new ConfirmWindow(primaryText, message, destructive: true)
            {
                Title = title
            };

            var owner = Owner();
            if (owner != null) window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            return await window.ShowDialog<bool>(owner)
                ? ConfirmOutcome.Primary
                : ConfirmOutcome.Cancelled;
        }

        private static Window? Owner() =>
            (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
    }
}