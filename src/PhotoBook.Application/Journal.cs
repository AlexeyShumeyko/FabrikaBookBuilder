using System;

namespace PhotoBookRenamer.Application
{
    /// <summary>
    /// The way code that cannot be handed a dependency records what went wrong.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three writers in the shell have no place to take a constructor argument: a static
    /// converter's cache miss, a background job's failure handler, a measurement dump. They
    /// were each writing their own file for that reason, which is what made the journal five
    /// files instead of one.
    /// </para>
    /// <para>
    /// This is deliberately not the service locator the campaign removed. It cannot be used to
    /// get behaviour - nothing here decides anything, and there is nothing to resolve beyond a
    /// sink with no dependencies. Before it is installed, every call is a no-op, so a library
    /// or a test that never installs one still runs.
    /// </para>
    /// </remarks>
    public static class Journal
    {
        private static IJournal _current = new NullJournal();

        /// <summary>The sink in force. Nothing, until one is installed.</summary>
        public static IJournal Current => _current;

        /// <summary>Installs the sink. Called once, while the program is starting.</summary>
        public static void Install(IJournal journal) => _current = journal ?? new NullJournal();

        /// <summary>Records an entry, whatever the sink is.</summary>
        public static void Write(JournalLevel level, string source, string message, Exception? error = null)
            => _current.Write(level, source, message, error);

        private sealed class NullJournal : IJournal
        {
            public void Write(JournalLevel level, string source, string message, Exception? error)
            {
            }
        }
    }
}
