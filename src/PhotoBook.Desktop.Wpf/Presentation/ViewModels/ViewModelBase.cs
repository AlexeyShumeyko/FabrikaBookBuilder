using System;
using PhotoBook.Core;

namespace PhotoBookRenamer.Presentation.ViewModels
{
    /// <summary>
    /// Base for the view models of the shell. The notification itself comes from
    /// <see cref="PhotoBook.Core.ObservableObject"/> in the domain assembly, which is what
    /// the entities use too, so a bound object behaves the same whether it is an entity or
    /// a view model.
    /// </summary>
    public abstract class ViewModelBase : ObservableObject
    {
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
        protected static void OnUiThread(Action action)
        {
            var app = System.Windows.Application.Current;
            if (app is null || app.Dispatcher.CheckAccess())
            {
                action();
                return;
            }

            app.Dispatcher.BeginInvoke(action);
        }
    }
}
