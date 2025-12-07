using PhotoBookRenamer.Domain;
using System.Threading.Tasks;

namespace PhotoBookRenamer.Application
{
    public interface IExportService
    {
        Task<bool> ExportProjectAsync(Project project, string outputFolder);
        string GenerateFileName(int bookIndex, int fileIndex);
    }
}





