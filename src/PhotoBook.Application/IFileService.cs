using System.Collections.Generic;
using System.Threading.Tasks;

namespace PhotoBookRenamer.Application
{
    /// <summary>
    /// Reading and writing files.
    ///
    /// <para>
    /// Only the file system. Choosing a file is a question for a person and lives behind
    /// <see cref="PhotoBookRenamer.Application.IPickFiles"/> in the shell.
    /// </para>
    /// </summary>
    public interface IFileService
    {
        Task<List<string>> GetJpegFilesAsync(string folderPath);

        Task<bool> ValidateFoldersAsync(List<string> folderPaths);

        Task<ValidationResult> ValidateFoldersDetailedAsync(List<string> folderPaths);

        Task CopyFileAsync(string source, string destination);

        bool IsJpegFile(string filePath);
    }
}
