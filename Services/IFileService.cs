using System.Collections.Generic;
using System.Threading.Tasks;

namespace PhotoBookRenamer.Services
{
    public interface IFileService
    {
        Task<List<string>> GetJpegFilesAsync(string folderPath);
        Task<bool> ValidateFoldersAsync(List<string> folderPaths);
        Task<ValidationResult> ValidateFoldersDetailedAsync(List<string> folderPaths);
        Task<string?> SelectFoldersAsync();
        Task<string?> SelectOutputFolderAsync(string? defaultPath = null);
        Task<string?> SelectOutputFolderWithNameAsync(string? defaultPath = null, string? defaultFolderName = null);
        Task<string[]?> SelectFilesAsync();
        Task CopyFileAsync(string source, string destination);
        bool IsJpegFile(string filePath);
    }
}





