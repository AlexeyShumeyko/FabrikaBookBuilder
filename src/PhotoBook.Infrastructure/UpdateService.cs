using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using PhotoBookRenamer.Application;
using Octokit;

namespace PhotoBookRenamer.Infrastructure
{
    /// <summary>
    /// The update feed on GitHub Releases.
    ///
    /// <para>
    /// Behaviour is unchanged from the version that was verified end to end: a release that
    /// cannot be reached is reported as "no update" rather than as an exception, the download
    /// goes to a temporary folder, an archive is unpacked and the installer inside it is
    /// started with the rights it asks for. What changed is the last step - this class used
    /// to close the program itself, which is why the layer that talks to GitHub could not be
    /// built without a user interface. It now returns the installer and lets the shell decide
    /// to leave.
    /// </para>
    /// </summary>
    internal class UpdateService : IUpdateFeed
    {
        private readonly GitHubClient _client;
        private readonly HttpClient _httpClient;

        private const string Owner = "AlexeyShumeyko";
        private const string Repo = "FabrikaBookBuilder";

        public UpdateService()
        {
            _client = new GitHubClient(new ProductHeaderValue("PhotoBookRenamer"));
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(10);
        }

        /// <summary>
        /// The running version of the PROGRAM, three numbers.
        /// </summary>
        /// <remarks>
        /// Read from the entry assembly, not from the assembly this class happens to live in.
        /// While the update feed was inside the executable those were the same assembly; after
        /// the layers became separate projects it stopped being, and this method answered
        /// "1.0.0" forever - the library's own version - so every launch offered an update
        /// that was already installed. The version of the program belongs to the program.
        /// </remarks>
        public string GetCurrentVersion()
        {
            var version = (Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly).GetName().Version;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        public async Task<bool> CheckForUpdatesAsync()
        {
            try
            {
                var latest = await GetLatestVersionAsync();
                if (string.IsNullOrEmpty(latest))
                    return false;

                var currentVersion = new Version(GetCurrentVersion());
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

        public async Task<string?> GetLatestReleaseNotesAsync()
        {
            try
            {
                var releases = await _client.Repository.Release.GetLatest(Owner, Repo);
                return releases.Body;
            }
            catch
            {
                return null;
            }
        }

        public async Task<string?> GetDownloadUrlAsync()
        {
            try
            {
                var releases = await _client.Repository.Release.GetLatest(Owner, Repo);

                foreach (var asset in releases.Assets)
                {
                    if (asset.Name == "BookBuilder-Studio-Setup.zip")
                        return asset.BrowserDownloadUrl;
                }

                foreach (var asset in releases.Assets)
                {
                    if (asset.Name.Contains("BookBuilder-Studio-Setup") && asset.Name.EndsWith(".zip"))
                        return asset.BrowserDownloadUrl;
                }

                foreach (var asset in releases.Assets)
                {
                    if (asset.Name.Contains("BookBuilder-Studio-Setup") && asset.Name.EndsWith(".exe"))
                        return asset.BrowserDownloadUrl;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<string?> DownloadInstallerAsync(
            string downloadUrl,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "PhotoBookRenamer", "Update");
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
                Directory.CreateDirectory(tempDir);

                var isZip = downloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                var isExe = downloadUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                var extension = isZip ? ".zip" : (isExe ? ".exe" : ".zip");
                var filePath = Path.Combine(tempDir, $"update{extension}");

                using (var response = await _httpClient.GetAsync(
                           downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? 0L;
                    var downloadedBytes = 0L;

                    using (var fileStream = new FileStream(filePath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                    using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                    {
                        var buffer = new byte[8192];
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                            downloadedBytes += bytesRead;

                            if (totalBytes > 0 && progress != null)
                            {
                                var percent = (double)downloadedBytes / totalBytes * 100;
                                progress.Report(percent);
                            }
                        }
                    }
                }

                if (isZip)
                {
                    var extractPath = Path.Combine(tempDir, "extracted");
                    Directory.CreateDirectory(extractPath);
                    ZipFile.ExtractToDirectory(filePath, extractPath);

                    var installerExe = Directory.GetFiles(extractPath, "*.exe", SearchOption.AllDirectories)
                        .FirstOrDefault(f => Path.GetFileName(f).Contains("BookBuilder-Studio-Setup"));

                    if (installerExe != null)
                    {
                        return StartElevated(installerExe);
                    }

                    var installerPath = Path.Combine(extractPath, "install.bat");
                    if (File.Exists(installerPath))
                    {
                        var info = new ProcessStartInfo
                        {
                            FileName = installerPath,
                            WorkingDirectory = extractPath,
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        Process.Start(info);
                        return installerPath;
                    }
                }

                if (isExe)
                {
                    return StartElevated(filePath);
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Не удалось загрузить обновление: {ex.Message}");
                return null;
            }
        }

        private static string StartElevated(string path)
        {
            var info = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(info);
            return path;
        }
    }
}
