using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PhotoBook.Application;

namespace PhotoBookRenamer.Presentation.Services
{
    /// <summary>
    /// The WPF answer to <see cref="IUiThread"/>: the application's dispatcher, and nothing
    /// else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The priority mapping is the only thing here worth reading. The names came from this
    /// engine's dispatcher, and Avalonia's queue has the same four shapes, so a shell that
    /// maps them differently will still honour what each one was asking for.
    /// </para>
    /// <para>
    /// BeginInvoke with no priority means <see cref="DispatcherPriority.Normal"/> in WPF, and
    /// <see cref="UiDispatchPriority.Normal"/> says the same thing. The default is spelled
    /// out anyway, because "normal" is not what "normal" means in WPF - the WPF default for
    /// a call with no argument is <c>DispatcherPriority.Normal</c>, and Avalonia's default
    /// is its own. Leaving it to the engine would have left the two quietly different.
    /// </para>
    /// </remarks>
    internal class WpfUiThread : IUiThread
    {
        public bool IsOnUiThread
        {
            get
            {
                var app = Application.Current;
                return app is null || app.Dispatcher.CheckAccess();
            }
        }

        public void Invoke(Action action)
        {
            var app = Application.Current;
            if (app is null || app.Dispatcher.CheckAccess())
            {
                action();
                return;
            }

            app.Dispatcher.Invoke(action);
        }

        public async Task InvokeAsync(Action action)
        {
            var app = Application.Current;
            if (app is null || app.Dispatcher.CheckAccess())
            {
                action();
                return;
            }

            await app.Dispatcher.InvokeAsync(action);
        }

        public void BeginInvoke(Action action, UiDispatchPriority priority = UiDispatchPriority.Normal)
        {
            var app = Application.Current;
            if (app is null)
            {
                action();
                return;
            }

            // Always queued, never run inline - including when the caller is already on the
            // UI thread. That is the whole point of a priority: "after layout" only means
            // something if the work waits for layout to happen. An early return for
            // CheckAccess() here (copied from the Invoke methods, where it is right) ran the
            // three measurements immediately, before layout, and the open-time numbers
            // quietly dropped from 504 elements to 213. The diagnostic was the test that
            // caught it.
            app.Dispatcher.BeginInvoke(Translate(priority), new Action(action));
        }

        private static DispatcherPriority Translate(UiDispatchPriority priority) => priority switch
        {
            UiDispatchPriority.Render => DispatcherPriority.Render,
            UiDispatchPriority.Background => DispatcherPriority.Background,
            UiDispatchPriority.ContextIdle => DispatcherPriority.ContextIdle,
            _ => DispatcherPriority.Normal
        };
    }
}
