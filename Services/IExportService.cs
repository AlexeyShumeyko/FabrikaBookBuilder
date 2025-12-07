using PhotoBookRenamer.Models;
using System.Threading.Tasks;

namespace PhotoBookRenamer.Services
{
    public interface IExportService
    {
        Task<bool> ExportProjectAsync(Project project, string outputFolder);
        string GenerateFileName(int bookIndex, int fileIndex);
    }
}





