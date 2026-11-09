using System;
using System.Threading.Tasks;

namespace PhotoBook.Application
{
    /// <summary>
    /// The thread a shell draws on, as something a view model can ask for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A view model has no business knowing that a dispatcher exists. It knows that the
    /// entity it just saved raises <c>PropertyChanged</c>, that a bound command re-reads its
    /// <c>CanExecute</c> from whatever thread that arrives on, and that arriving on the
    /// wrong thread throws. The first fact is a reason; the dispatcher is an implementation
    /// detail of the engine that draws.
    /// </para>
    /// <para>
    /// That is not theoretical tidiness. A save that resumed on the thread pool raised a
    /// notification, a WPF command re-evaluated from there, and the exception unwound the
    /// automatic save half-done. This contract exists so that the fix lives in one place
    /// instead of in every call site that can forget it.
    /// </para>
    /// <para>
    /// All three methods behave the same way if the caller is already on the UI thread -
    /// which matters, because code that has to work either way is normal, not exceptional.
    /// </para>
    /// </remarks>
    public interface IUiThread
    {
        /// <summary>True when the caller is already on the thread that draws.</summary>
        bool IsOnUiThread { get; }

        /// <summary>
        /// Runs <paramref name="action"/> on the UI thread and waits for it. Use when the
        /// next line depends on it having happened.
        /// </summary>
        void Invoke(Action action);

        /// <summary>
        /// Runs <paramref name="action"/> on the UI thread and completes when it has.
        /// </summary>
        Task InvokeAsync(Action action);

        /// <summary>
        /// Queues <paramref name="action"/> and returns immediately. Use for work whose
        /// result nobody is waiting on - a measurement, a repaint request.
        /// </summary>
        void BeginInvoke(Action action, UiDispatchPriority priority = UiDispatchPriority.Normal);
    }
}
