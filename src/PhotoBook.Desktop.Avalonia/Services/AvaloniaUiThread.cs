using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using PhotoBook.Application;

namespace PhotoBook.Shell.Services
{
    /// <summary>
    /// The Avalonia answer to <see cref="IUiThread"/>: the framework's dispatcher.
    /// </summary>
    /// <remarks>
    /// BeginInvoke always queues, even when the caller is already on the thread - see the
    /// note on the WPF side, where copying the early return from Invoke made three
    /// measurements run before layout and quietly changed the numbers.
    /// </remarks>
    public class AvaloniaUiThread : IUiThread
    {
        public bool IsOnUiThread => Dispatcher.UIThread.CheckAccess();

        public void Invoke(Action action)
        {
            if (action is null) return;
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
                return;
            }

            Dispatcher.UIThread.Invoke(action);
        }

        public async Task InvokeAsync(Action action)
        {
            if (action is null) return;
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(action);
        }

        public void BeginInvoke(
            Action action,
            UiDispatchPriority priority = UiDispatchPriority.Normal)
        {
            if (action is null) return;

            var translated = Translate(priority);
            if (Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(action, translated);
                return;
            }

            Dispatcher.UIThread.Post(action, translated);
        }

        private static DispatcherPriority Translate(UiDispatchPriority priority) => priority switch
        {
            UiDispatchPriority.Render => DispatcherPriority.Render,
            UiDispatchPriority.Background => DispatcherPriority.Background,
            UiDispatchPriority.ContextIdle => DispatcherPriority.Loaded,
            _ => DispatcherPriority.Normal
        };
    }
}
