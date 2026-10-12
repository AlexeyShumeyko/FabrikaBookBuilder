using System;
using System.IO;
using PhotoBookRenamer.Application;

namespace PhotoBookRenamer.Infrastructure
{
    /// <summary>
    /// How diagnostics are put in place, so the composition root does not name an
    /// implementation.
    /// </summary>
    /// <remarks>
    /// The journal is not resolved from the container: the shell has static writers - a
    /// converter's cache miss, a background failure - that have nowhere to take a
    /// constructor argument, so they reach it through <see cref="Journal"/>. That is the one
    /// thing in the program that is installed rather than injected, and it is installed here
    /// so that the concrete file journal stays internal like every other adapter.
    /// </remarks>
    public static class Diagnostics
    {
        /// <summary>The folder every diagnostic file lives in, next to the projects.</summary>
        public static string Directory =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhotoBookRenamer");

        /// <summary>The journal a customer is asked to send. The same path every time.</summary>
        public static string JournalPath => Path.Combine(Directory, "journal.log");

        /// <summary>
        /// Sends the journal to disk. Informational entries are dropped unless
        /// FBR_JOURNAL_VERBOSE is set: a support file full of routine lines hides the two
        /// entries that explain the report.
        /// </summary>
        public static void InstallJournal()
        {
            var verbose = !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("FBR_JOURNAL_VERBOSE"));

            Journal.Install(new FileJournal(Directory, verbose));
        }
    }
}
