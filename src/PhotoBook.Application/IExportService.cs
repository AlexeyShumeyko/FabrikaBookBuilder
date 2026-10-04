using PhotoBook.Core;
using System.Threading.Tasks;

namespace PhotoBookRenamer.Application
{
    public interface IExportService
    {
        Task<bool> ExportProjectAsync(Project project, string outputFolder);

        /// <summary>
        /// Export with explicit options (per-book subfolders, progress reporting).
        /// <see cref="ExportProjectAsync(Project, string)"/> is equivalent to passing
        /// <c>new ExportOptions()</c>, i.e. a flat folder.
        /// </summary>
        Task<bool> ExportProjectAsync(Project project, string outputFolder, ExportOptions options);

        string GenerateFileName(int bookIndex, int fileIndex);
    }
}





