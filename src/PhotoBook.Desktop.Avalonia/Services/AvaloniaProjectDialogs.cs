using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using PhotoBook.Application;
using PhotoBook.Core;
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
            // The heading on screen is what the question is about. The window keeps its own
            // name, the same for every question in the program, so that "is a question open?"
            // is something that can be asked at all.
            var window = new ConfirmWindow(title, confirmText, message, destructive);

            var owner = Owner();
            if (owner != null) window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            return await window.ShowDialog<bool>(owner);
        }

        /// <summary>
        /// The one question this screen asks, in the panel the prototype draws for it: the
        /// project named on a card of its own, the promise about the photographs, and two
        /// answers - the safe one and the red one.
        /// </summary>
        public async Task<bool> ConfirmDeleteProjectAsync(ProjectInfo project)
        {
            var main = Owner() as MainWindow;
            if (main is null)
            {
                // No window to lay a panel over - never reached in the running program, and
                // still a question that must be asked rather than a deletion taken on trust.
                return await ConfirmAsync(
                    "Удалить проект?",
                    $"Проект \"{project.Name}\" будет удалён вместе со своей структурой.",
                    "Удалить проект");
            }

            var answer = await main.ShowSheetAsync(new DeleteProjectSheet(project));
            return answer is not null;
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
            var window = new ConfirmWindow(title, primaryText, message, destructive: true)
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
