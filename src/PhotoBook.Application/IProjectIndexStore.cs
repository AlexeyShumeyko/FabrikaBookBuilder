using System.Threading;
using System.Threading.Tasks;

namespace PhotoBook.Application
{
    /// <summary>
    /// Where the project index lives and how it is written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The index is one JSON file listing the user's projects. Before this existed the
    /// service that decided what a project is also decided where the file sat, built the
    /// path, created the folder and performed the replace - <c>System.IO</c> calls spread
    /// through the layer. Wave 1 step 1.3 extracted <c>IProjectRepository</c> for the project
    /// files and the step was closed on the strength of the type existing; this is the half
    /// that was left behind.
    /// </para>
    /// <para>
    /// The port hands back and takes <em>text</em>, not projects. Serialising is this layer's
    /// business; the file format of the index is not, and nothing above it should have to
    /// care whether the index is one file, two, or a key in a registry.
    /// </para>
    /// <para>
    /// <see cref="WriteAsync"/> must be atomic. The index is rewritten by a
    /// read-modify-write from several places, and a write that is interrupted half way
    /// leaves a truncated file - which is what made the project list come back empty once
    /// already. Writing to a temporary file and replacing the target is not an optimisation,
    /// it is the whole contract.
    /// </para>
    /// </remarks>
    public interface IProjectIndexStore
    {
        /// <summary>
        /// Where the project with this id is stored. The store owns the layout of the folder,
        /// so this is a question for it rather than a path assembled by the caller.
        /// </summary>
        string GetProjectFilePath(string projectId);

        /// <summary>The index's contents, or null when there is no index yet.</summary>
        Task<string?> ReadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Replaces the index with <paramref name="json"/>. Atomic: after this returns, the
        /// index is either the old contents or the new ones, never something in between.
        /// </summary>
        Task WriteAsync(string json, CancellationToken cancellationToken = default);

        /// <summary>Removes one project's file. False when there was nothing to remove.</summary>
        bool DeleteProjectFile(string projectId);

        /// <summary>
        /// The pre-1.0 index for one mode, which kept folders and combined runs in separate
        /// files. Null when there is none.
        /// </summary>
        Task<string?> ReadLegacyAsync(string mode, CancellationToken cancellationToken = default);
    }
}
