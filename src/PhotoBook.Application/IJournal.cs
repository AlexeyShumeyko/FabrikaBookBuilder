using System;

namespace PhotoBookRenamer.Application
{
    /// <summary>How serious a journal entry is.</summary>
    public enum JournalLevel
    {
        Information,
        Warning,
        Error
    }

    /// <summary>
    /// One place where everything the program wants recorded ends up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The product had five sinks before this: the console, and four hand-written files in
    /// two different roots - two of them under %TEMP%, which Windows is free to empty. The
    /// twenty-one <c>LogError</c> calls in the editors went to the console, and a desktop
    /// program started from a shortcut has no console, so the failure a customer reported was
    /// never written down anywhere.
    /// </para>
    /// <para>
    /// One sink matters for support as much as for development: when a customer says "it did
    /// not work", the answer has to be in a file we can ask them to send, and it has to be the
    /// same file every time.
    /// </para>
    /// </remarks>
    public interface IJournal
    {
        /// <param name="level">How serious the entry is.</param>
        /// <param name="source">Where it came from: the operation, not the class.</param>
        /// <param name="message">What happened, in the words a reader will understand.</param>
        /// <param name="error">The exception, when there is one.</param>
        void Write(JournalLevel level, string source, string message, Exception? error = null);
    }
}
