using System;
using System.Threading.Tasks;
using Octokit;

namespace PhotoBookRenamer.Services
{
    public class UpdateService : IUpdateService
    {
        private readonly GitHubClient _client;
        private const string Owner = "YourGitHubUsername";
        private const string Repo = "PhotoBookRenamer";

        public UpdateService()
        {
            _client = new GitHubClient(new ProductHeaderValue("PhotoBookRenamer"));
        }

        public async Task<bool> CheckForUpdatesAsync()
        {
            try
            {
                var latest = await GetLatestVersionAsync();
                if (string.IsNullOrEmpty(latest))
                    return false;

                var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                var latestVersion = new Version(latest);

                return latestVersion > currentVersion;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string?> GetLatestVersionAsync()
        {
            try
            {
                var releases = await _client.Repository.Release.GetLatest(Owner, Repo);
                return releases.TagName.TrimStart('v');
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> DownloadUpdateAsync(string downloadUrl)
        {
            // Реализация загрузки обновления
            await Task.CompletedTask;
            return false;
        }
    }
}





