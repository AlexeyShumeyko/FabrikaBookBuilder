using PhotoBook.Core;
using System.Threading.Tasks;

namespace PhotoBook.Application
{
    public interface IExportService
    {
        Task<Result> ExportProjectAsync(Project project, string outputFolder);

        /// <summary>
        /// Export with explicit options (per-book subfolders, progress reporting).
        /// <see cref="ExportProjectAsync(Project, string)"/> is equivalent to passing
        /// <c>new ExportOptions()</c>, i.e. a flat folder.
        /// </summary>
        Task<Result> ExportProjectAsync(Project project, string outputFolder, ExportOptions options);

        string GenerateFileName(int bookIndex, int fileIndex);
    }
}





