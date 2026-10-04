using System.Threading.Tasks;
using PhotoBook.Core;

namespace PhotoBookRenamer.Application
{
    /// <summary>
    /// Reading and writing a project file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A port because the format is a promise rather than a detail. Project files written by
    /// version 1.1.1 have to keep opening, and files written today have to keep opening after
    /// the user interface has been replaced. That promise belongs to whoever reads the file,
    /// so the shell must not be the one parsing it.
    /// </para>
    /// <para>
    /// Opening a project is not only parsing. A file written before photographs carried their
    /// pixel size arrives without it, and the card frames are shaped from those numbers, so
    /// the repository finishes what the old file left out - reading the headers of the
    /// photographs, which the caller would otherwise have to remember to do, at every one of
    /// the three places a project is opened.
    /// </para>
    /// </remarks>
    public interface IProjectRepository
    {
        /// <summary>
        /// Writes the project, creating the folder if it is not there yet.
        /// </summary>
        /// <exception cref="System.Exception">
        /// With a message a person can act on, when the file cannot be written. A save that
        /// failed silently is a project that is gone.
        /// </exception>
        Task SaveAsync(Project project, string filePath);

        /// <summary>
        /// Opens the project, or answers null when there is nothing to open - a file that does
        /// not exist, or an empty one. A missing file is not an error: it is a project that
        /// has not been saved yet.
        /// </summary>
        /// <exception cref="System.Exception">
        /// With a message a person can act on, when a file exists but cannot be read. The
        /// caller decides whether to fall back to an empty project.
        /// </exception>
        Task<Project?> LoadAsync(string filePath);
    }
}
