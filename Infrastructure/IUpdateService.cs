using System;
using System.Threading.Tasks;

namespace PhotoBookRenamer.Infrastructure
{
    public interface IUpdateService
    {
        Task<bool> CheckForUpdatesAsync();
        Task<string?> GetLatestVersionAsync();
        Task<string?> GetLatestReleaseNotesAsync();
        Task<string?> GetDownloadUrlAsync();
        Task<bool> DownloadAndInstallUpdateAsync(string downloadUrl, IProgress<double>? progress = null);
        string GetCurrentVersion();
    }
}





