using System.Collections.Generic;
using System.Threading.Tasks;

namespace PhotoBook.Application
{
    /// <summary>
    /// Reading and writing files.
    ///
    /// <para>
    /// Only the file system. Choosing a file is a question for a person and lives behind
    /// <see cref="PhotoBook.Application.IPickFiles"/> in the shell.
    /// </para>
    /// </summary>
    public interface IFileService
    {
        Task<List<string>> GetJpegFilesAsync(string folderPath);


        Task<ValidationResult> ValidateFoldersDetailedAsync(List<string> folderPaths);

        Task CopyFileAsync(string source, string destination);

        bool IsJpegFile(string filePath);

        /// <summary>
        /// Whether a folder is there. The application layer asks this when deciding a
        /// project's status - a finished run whose output folder has since been deleted is
        /// not finished - and reaching for <c>System.IO.Directory</c> itself is precisely
        /// what this port exists to stop.
        /// </summary>
        bool DirectoryExists(string path);
    }
}
