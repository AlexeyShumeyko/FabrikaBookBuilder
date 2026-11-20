using System;
using System.IO;
using PhotoBook.Application;

namespace PhotoBook.Infrastructure
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
        /// True when routine entries are kept as well as failures. FBR_JOURNAL_VERBOSE turns
        /// them on: a support file full of routine lines hides the two entries that explain
        /// the report.
        /// </summary>
        public static bool Verbose =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FBR_JOURNAL_VERBOSE"));

        /// <summary>
        /// Builds the sink. One factory, because there are two of them: the shell installs one
        /// for its static writers, and the container has one for everything it injects. They
        /// used to be constructed separately and the container's was built without the
        /// verbosity flag - so setting FBR_JOURNAL_VERBOSE turned verbose entries on for half
        /// the program, which reads as "the check never ran" rather than "the setting is
        /// partial".
        /// </summary>
        public static IJournal Create() => new FileJournal(Directory, Verbose);

        /// <summary>
        /// Sends the journal to disk for the code that cannot be handed one.
        /// </summary>
        public static void InstallJournal() => Journal.Install(Create());
    }
}
