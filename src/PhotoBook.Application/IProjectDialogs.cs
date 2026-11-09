using System.Threading.Tasks;

namespace PhotoBook.Application
{
    /// <summary>
    /// Asking the user a question that needs an answer before work continues.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A dialog is a window, and a window belongs to an engine. But the thing being asked -
    /// "save before leaving?", "delete this project?" - is the same in every engine, and the
    /// code that needs the answer should not have to name the type of the window that will
    /// collect it. So the contract lives here and each shell supplies the windows.
    /// </para>
    /// <para>
    /// <b>Every method is asynchronous, and that is not a style choice.</b> WPF's
    /// <c>ShowDialog</c> blocks the calling thread inside its own nested message loop, so a
    /// synchronous contract reads as natural there. Avalonia's returns a task and does not
    /// block, and blocking on that task from the UI thread deadlocks - the dialog closes,
    /// completes a task nobody is waiting on, and the window freezes with the dialog gone.
    /// A contract written for the first engine would have been a hang in the second.
    /// </para>
    /// <para>
    /// Three shapes rather than one: a yes/no, something to read and acknowledge, and a
    /// three-way question where the third answer - stay here - is the important one. The
    /// three-way case used to be a <c>bool</c> in some places and a magic string in others,
    /// and both hid the same decision.
    /// </para>
    /// <para>
    /// No implementation lives in this layer, and none should: a shell's dialogs cannot be
    /// reused by another shell anyway, because a window cannot cross an engine.
    /// </para>
    /// </remarks>
    public interface IProjectDialogs
    {
        /// <summary>
        /// Asks a yes/no question. True only for the confirm button - closing the window
        /// with Escape or its close button counts as "no", as it must for anything
        /// destructive.
        /// </summary>
        Task<bool> ConfirmAsync(
            string title,
            string message,
            string confirmText = "Подтвердить",
            bool destructive = true);

        /// <summary>
        /// Says something the user has to read and acknowledge. One button, so it reads as
        /// the same window as the questions and not as a message from another program.
        /// </summary>
        Task NotifyAsync(
            string title,
            string message,
            string okText = "Понятно",
            bool destructive = false);

        /// <summary>
        /// Asks a question with three answers, where the third is "stay where you are".
        /// </summary>
        Task<ConfirmOutcome> AskAsync(
            string title,
            string message,
            string primaryText,
            string secondaryText);
    }
}
