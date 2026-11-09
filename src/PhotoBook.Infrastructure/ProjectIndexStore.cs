using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application;

namespace PhotoBook.Infrastructure
{
    /// <summary>
    /// The project index on disk: one JSON file under the user's own profile.
    /// </summary>
    /// <remarks>
    /// The path is decided here rather than in the service above, because this is the only
    /// place that can be wrong about it. It used to be assembled in
    /// <c>ProjectListService</c>, inside the layer that decides what a project is.
    /// </remarks>
    internal class ProjectIndexStore : IProjectIndexStore
    {
        private readonly string _directory;
        private readonly string _indexPath;

        public ProjectIndexStore()
            : this(CombineDefaultPath())
        {
        }

        /// <summary>
        /// Takes the directory rather than the index path, so that a test can point the whole
        /// store at a temporary folder and be certain it is not touching the real index.
        /// </summary>
        internal ProjectIndexStore(string directory)
        {
            _directory = directory;
            _indexPath = Path.Combine(directory, "projects.json");

            // Created once, at construction, and swallowed on failure: a store that cannot
            // create its folder still reads, and the write is where the error belongs.
            if (!Directory.Exists(_directory))
            {
                try
                {
                    Directory.CreateDirectory(_directory);
                }
                catch (Exception)
                {
                    // Deliberately empty. The first write will fail with a real message and
                    // the journal will hold it; throwing here would take the whole program
                    // down over a folder that is created on first use anyway.
                }
            }
        }

        /// <summary>
        /// Under the user's profile rather than beside the executable: the program is
        /// installed once for everyone and the projects belong to the person who made them.
        /// </summary>
        private static string CombineDefaultPath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoBookRenamer",
            "Projects");

        public string GetProjectFilePath(string projectId) =>
            Path.Combine(_directory, $"{projectId}.json");

        public async Task<string?> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_indexPath)) return null;

            try
            {
                return await File.ReadAllTextAsync(_indexPath, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                // It went away between the check and the read. A missing index is an empty
                // list, not an error.
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }

        public async Task WriteAsync(string json, CancellationToken cancellationToken = default)
        {
            // Write to a sibling first and then replace. File.WriteAllTextAsync truncates the
            // target before it writes, so a crash or a second writer leaves a partial index -
            // and a partial index reads as an empty project list, which is how projects
            // disappeared once already. File.Move over an existing file is a rename, and on
            // NTFS that is atomic: a reader sees the old contents or the new ones.
            var temporaryPath = _indexPath + ".tmp";

            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, _indexPath, overwrite: true);
        }

        public bool DeleteProjectFile(string projectId)
        {
            var path = Path.Combine(_directory, $"{projectId}.json");
            if (!File.Exists(path)) return false;

            File.Delete(path);
            return true;
        }

        public async Task<string?> ReadLegacyAsync(string mode, CancellationToken cancellationToken = default)
        {
            var path = Path.Combine(_directory, $"{mode}_projects.json");
            if (!File.Exists(path)) return null;

            try
            {
                return await File.ReadAllTextAsync(path, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }
    }
}
