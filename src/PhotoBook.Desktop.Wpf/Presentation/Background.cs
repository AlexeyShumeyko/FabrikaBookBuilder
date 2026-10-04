using System;
using System.Threading.Tasks;

namespace PhotoBookRenamer.Presentation
{
    /// <summary>
    /// Runs work in the background without letting it take the program down with it.
    ///
    /// <para>
    /// The app is full of fire-and-forget calls - "load the thumbnails", "measure the
    /// photos", "save the index" - and every one of them was written as
    /// <c>_ = SomethingAsync();</c>. That is a trap in .NET: the returned task is never
    /// awaited and nobody observes its exception, and the default escalation in .NET Core
    /// is to <b>terminate the process</b>. So a single unreadable JPEG, a full disk, or a
    /// file that disappeared between the scan and the copy killed the whole program with
    /// "unhandled exception" and no dialog. That is not a hypothetical: a
    /// <c>NullReferenceException</c> in the open path did exactly that during this work.
    /// </para>
    ///
    /// <para>
    /// So every fire-and-forget call in the ViewModels goes through here. The exception is
    /// caught, written to the log, and the program carries on - which is the behaviour a
    /// photographer needs: a broken file must not lose the work.
    /// </para>
    /// </summary>
    internal static class Background
    {
        /// <summary>
        /// Runs <paramref name="work"/> in the background; a failure is logged, never fatal.
        /// </summary>
        public static void Run(Func<Task> work, string what)
        {
            if (work == null) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await work().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Report(what, ex);
                }
            });
        }

        /// <summary>
        /// Starts <paramref name="work"/> and returns its task so the caller can await it
        /// when it wants to; a failure is logged rather than crashing either way.
        /// </summary>
        public static async Task SafeAsync(Func<Task> work, string what)
        {
            if (work == null) return;

            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Report(what, ex);
            }
        }

        private static void Report(string what, Exception ex)
        {
            try
            {
                string line =
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  background '{what}' failed: {ex.GetType().Name}: {ex.Message}";
                System.IO.File.AppendAllText(LogPath(), line + Environment.NewLine);
            }
            catch
            {
                // Nothing left to do: the program must not die over its own diagnostics.
            }
        }

        private static string LogPath()
        {
            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhotoBookRenamer");
            System.IO.Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "background-errors.log");
        }
    }
}
