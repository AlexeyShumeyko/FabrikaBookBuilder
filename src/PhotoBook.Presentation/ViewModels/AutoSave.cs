using System;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>
    /// Writes the open project a moment after the last change, and once at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The editors no longer have a save button, so this is the only thing that puts a
    /// project on disk while somebody is working on it. Every change asks for a save;
    /// requests that arrive in a burst - dragging five photographs into five slots, a
    /// structure that grows by ten books - collapse into one write.
    /// </para>
    /// <para>
    /// Writes are chained, never parallel: two saves of one project at the same time can
    /// each read a half-updated model and leave the older answer on disk. A quiet period
    /// that is interrupted by another change does not write at all; the newer request
    /// takes over, so the file always reflects the last state, not an intermediate one.
    /// </para>
    /// <para>
    /// <see cref="FlushAsync"/> is what navigation and closing the window use: it writes
    /// what is pending without waiting for the quiet period and waits for a write that is
    /// already running. Leaving a screen therefore cannot outrun the save.
    /// </para>
    /// </remarks>
    public sealed class AutoSave : IDisposable
    {
        /// <summary>
        /// How long the editor waits for the work to pause. Short enough that a crash or a
        /// power cut costs the last gesture, long enough that a run of drops is one write.
        /// </summary>
        public static readonly TimeSpan DefaultQuietPeriod = TimeSpan.FromMilliseconds(350);

        private readonly IUiThread _ui;
        private readonly Func<Task> _save;
        private readonly TimeSpan _quietPeriod;
        private readonly object _gate = new();

        private CancellationTokenSource? _pending;
        private Task _lastWrite = Task.CompletedTask;
        private bool _dirty;
        private bool _disposed;

        public AutoSave(IUiThread ui, Func<Task> save, TimeSpan? quietPeriod = null)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _save = save ?? throw new ArgumentNullException(nameof(save));
            _quietPeriod = quietPeriod ?? DefaultQuietPeriod;
        }

        /// <summary>
        /// Something worth keeping has changed. The write happens after the quiet period,
        /// or as soon as anything flushes.
        /// </summary>
        public void Request()
        {
            lock (_gate)
            {
                if (_disposed) return;

                _dirty = true;

                // A newer change supersedes the pending one; the save that was going to run
                // would have written a state that no longer exists. The timer's own task
                // disposes the token source when it exits - disposing here would race with
                // the delay that is still using it.
                _pending?.Cancel();

                var pending = new CancellationTokenSource();
                _pending = pending;

                _ = WriteAfterQuietPeriodAsync(pending);
            }
        }

        /// <summary>
        /// Writes what is pending now and waits for any write already running. Answers
        /// whether anything was written, so a caller that also wants an unconditional
        /// write does not write the same project twice.
        /// </summary>
        public async Task<bool> FlushAsync()
        {
            CancellationTokenSource? pending;
            Task? running = null;
            bool started;

            lock (_gate)
            {
                if (_disposed) return false;

                pending = _pending;
                _pending = null;

                if (_dirty)
                {
                    _dirty = false;
                    running = _lastWrite = WriteAfterAsync(_lastWrite);
                    started = true;
                }
                else
                {
                    started = false;
                    if (!_lastWrite.IsCompleted) running = _lastWrite;
                }
            }

            pending?.Cancel();

            if (running != null)
                await running.ConfigureAwait(false);

            return started || running != null;
        }

        /// <summary>
        /// Forgets a pending write. Used when the project is being replaced or reset: a
        /// timer that fired a moment later would write the empty screen over the project
        /// that is being closed.
        /// </summary>
        public void Cancel()
        {
            lock (_gate)
            {
                _pending?.Cancel();
                _pending = null;
                _dirty = false;
            }
        }

        public void Dispose()
        {
            lock (_gate) _disposed = true;
            Cancel();
        }

        private async Task WriteAfterQuietPeriodAsync(CancellationTokenSource pending)
        {
            try
            {
                try
                {
                    await Task.Delay(_quietPeriod, pending.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                lock (_gate)
                {
                    // A newer request owns the write now.
                    if (_pending != pending) return;
                    _pending = null;
                }

                try
                {
                    await WriteIfDirtyAsync().ConfigureAwait(false);
                }
                catch
                {
                    // The save reports its own failure to the editor, which shows it and
                    // keeps it in the journal. A timer has nobody to throw to.
                }
            }
            finally
            {
                pending.Dispose();
            }
        }

        private Task WriteIfDirtyAsync()
        {
            lock (_gate)
            {
                if (!_dirty) return _lastWrite;

                _dirty = false;
                return _lastWrite = WriteAfterAsync(_lastWrite);
            }
        }

        private async Task WriteAfterAsync(Task previous)
        {
            try
            {
                await previous.ConfigureAwait(false);
            }
            catch
            {
                // A failed earlier write must not stop this one: the project is still open
                // and the next change deserves another try.
            }

            await OnUiAsync(_save).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts the save on the thread that draws. The editors copy their observable
        /// collections into the project before the first await, and two threads touching
        /// one <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/> is a
        /// crash that would happen on somebody else's computer.
        /// </summary>
        private Task OnUiAsync(Func<Task> work)
        {
            if (_ui.IsOnUiThread) return work();

            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            _ui.Invoke(() =>
            {
                _ = RunOnUiAsync();

                async Task RunOnUiAsync()
                {
                    try
                    {
                        await work().ConfigureAwait(true);
                        done.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        done.TrySetException(ex);
                    }
                }
            });

            return done.Task;
        }
    }
}
