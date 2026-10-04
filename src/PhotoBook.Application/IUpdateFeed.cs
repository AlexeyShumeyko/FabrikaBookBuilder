using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoBookRenamer.Application
{
    /// <summary>
    /// Whether a newer version exists, and how to get it.
    ///
    /// <para>
    /// A port, because knowing where an update lives is not the same as deciding when to stop
    /// being the program that is running. The implementation downloaded the update, started
    /// the installer and then closed the application, all from inside the layer that talks
    /// to GitHub - which is why the file layer could not be built without a user interface.
    /// Now the implementation hands back the installer and the shell decides to leave.
    /// </para>
    ///
    /// <para>
    /// None of these methods throws for a release that cannot be reached. A machine that is
    /// offline, a repository that has moved, a rate limit: all of them answer "no update"
    /// rather than an exception, because failing to check must never stop the program from
    /// starting.
    /// </para>
    /// </summary>
    public interface IUpdateFeed
    {
        /// <summary>The running version, as three numbers.</summary>
        string GetCurrentVersion();

        /// <summary>True when the newest available version is greater than the running one.</summary>
        Task<bool> CheckForUpdatesAsync();

        /// <summary>The newest available version without its leading "v", or null.</summary>
        Task<string?> GetLatestVersionAsync();

        /// <summary>
        /// The text of that release, which is what the update window shows. Written by hand
        /// and free of links - see docs/decisions/0006-release-notes-are-user-facing.md.
        /// </summary>
        Task<string?> GetLatestReleaseNotesAsync();

        /// <summary>Where the installer can be downloaded from, or null.</summary>
        Task<string?> GetDownloadUrlAsync();

        /// <summary>
        /// Downloads the update, unpacks it if it is an archive, starts the installer with the
        /// rights it asks for, and returns the path it started.
        /// </summary>
        /// <remarks>
        /// It does not close the program. The installer replaces the running files, so the
        /// caller has to end the session - and that decision belongs to the shell, which is
        /// the only layer that knows what the session is.
        /// </remarks>
        Task<string?> DownloadInstallerAsync(
            string downloadUrl,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
