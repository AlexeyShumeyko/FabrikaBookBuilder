using System;
using System.IO;
using PhotoBook.Core;

namespace PhotoBook.Infrastructure
{
    /// <summary>
    /// Where an export is allowed to land.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule is the product's, not a window's: every export gets its own folder, so
    /// exporting the same project five times leaves five folders and two runs can never be
    /// mixed up. Choosing the parent by hand must not be a way to get the old
    /// overwrite-everything behaviour back.
    /// </para>
    /// <para>
    /// It lives here rather than in either shell because both shells ask the same question
    /// and a copy in each is two places to drift apart.
    /// </para>
    /// </remarks>
    public static class ExportFolderNaming
    {
        /// <summary>What a project's export folder is called before an index is added.</summary>
        public const string BaseName = "PhotoBookExport";

        /// <summary>
        /// The first folder named <paramref name="base"/>, <c>base1</c>, <c>base2</c>, ...
        /// under <paramref name="parent"/> that does not exist yet.
        /// </summary>
        /// <remarks>
        /// The plain name is used while it is free and the index starts at 1, so the first
        /// export on a clean desktop is <c>PhotoBookExport</c> and not
        /// <c>PhotoBookExport0</c>. A project's own output folder is only a starting point
        /// for the base name: the target is still a folder that does not exist yet.
        /// </remarks>
        public static string UniqueExportFolder(string parent, string? preferredBase = null)
        {
            string root = Path.Combine(parent, Name(preferredBase));

            if (!Exists(root)) return root;

            for (int i = 1; i < 10_000; i++)
            {
                string candidate = Path.Combine(parent, $"{Name(preferredBase)}{i}");
                if (!Exists(candidate)) return candidate;
            }

            // Ten thousand exports into one parent is not a case worth a different failure
            // mode; a timestamp is better than overwriting somebody's folder.
            return Path.Combine(parent, $"{Name(preferredBase)}-{DateTime.Now:yyyyMMdd-HHmmss}");
        }

        /// <summary>The base name a project asks for, or the default when it asks for nothing.</summary>
        public static string NameFor(Project project)
        {
            if (string.IsNullOrWhiteSpace(project.OutputFolder)) return BaseName;

            string name = Path.GetFileName(project.OutputFolder.TrimEnd(Path.DirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(name) ? BaseName : name!;
        }

        private static string Name(string? preferredBase) =>
            string.IsNullOrWhiteSpace(preferredBase) ? BaseName : preferredBase!;

        private static bool Exists(string path) => Directory.Exists(path) || File.Exists(path);
    }
}
