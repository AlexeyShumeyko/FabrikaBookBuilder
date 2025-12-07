using System.Threading.Tasks;

namespace PhotoBookRenamer.Services
{
    public interface IUpdateService
    {
        Task<bool> CheckForUpdatesAsync();
        Task<string?> GetLatestVersionAsync();
        Task<bool> DownloadUpdateAsync(string downloadUrl);
    }
}





