using System;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Core;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>
    /// Base for the view models of the shell. The notification itself comes from
    /// <see cref="PhotoBook.Core.ObservableObject"/> in the domain assembly, which is what
    /// the entities use too, so a bound object behaves the same whether it is an entity or
    /// a view model.
    /// </summary>
    public abstract class ViewModelBase : ObservableObject
    {
        private static readonly IUiThread RunsWhereverYouAre = new InlineUiThread();

        private IUiThread _ui = RunsWhereverYouAre;

        /// <summary>
        /// The shell's drawing thread. Set by the view model that needs it.
        /// </summary>
        /// <remarks>
        /// The default runs the action on the caller's own thread. That is deliberate: it is
        /// exactly what the previous code did when there was no application object, and it is
        /// the right answer for a view model that has nothing to marshal. It is the wrong
        /// answer for one that does - which is why every view model reaching a dispatcher
        /// sets this from the container.
        ///
        /// Assigning null puts the default back rather than leaving a field that will throw
        /// at the first notification.
        /// </remarks>
        protected IUiThread Ui
        {
            get => _ui;
            set => _ui = value ?? RunsWhereverYouAre;
        }

        /// <summary>
        /// Runs an action on the UI thread, or right now if we are already on it.
        /// </summary>
        /// <remarks>
        /// The domain raises PropertyChanged on whichever thread changed the book - a save
        /// that resumed on the thread pool, a background decode, a command's own
        /// continuation. Asking a WPF command to re-evaluate its CanExecute from there throws,
        /// and the exception unwinds whatever was running: the journal caught an automatic
        /// save aborting half-done with "the calling thread cannot access this object".
        /// </remarks>
        protected void RunOnUi(Action action)
        {
            if (action is null) return;
            _ui.Invoke(action);
        }

        /// <summary>
        /// The no-application case: there is no other thread to move to, so the action runs
        /// where it was called. A unit test is the usual caller.
        /// </summary>
        private sealed class InlineUiThread : IUiThread
        {
            public bool IsOnUiThread => true;

            public void Invoke(Action action) => action();

            public Task InvokeAsync(Action action)
            {
                action();
                return Task.CompletedTask;
            }

            public void BeginInvoke(
                Action action,
                UiDispatchPriority priority = UiDispatchPriority.Normal) => action();
        }
    }
}
