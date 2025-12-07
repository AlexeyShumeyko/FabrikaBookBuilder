using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Octokit;

namespace PhotoBookRenamer.Services
{
    public class UpdateService : IUpdateService
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

        public string GetCurrentVersion()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
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
                var version = releases.TagName.TrimStart('v');
                
                // Ищем EXE файл с установщиком (приоритет) или ZIP файл (fallback)
                foreach (var asset in releases.Assets)
                {
                    if (asset.Name.Contains("BookBuilder-Studio-Setup") && asset.Name.EndsWith(".exe"))
                    {
                        return asset.BrowserDownloadUrl;
                    }
                }
                
                // Fallback на ZIP, если EXE не найден
                foreach (var asset in releases.Assets)
                {
                    if (asset.Name.Contains("BookBuilder-Studio-Setup") && asset.Name.EndsWith(".zip"))
                    {
                        return asset.BrowserDownloadUrl;
                    }
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> DownloadAndInstallUpdateAsync(string downloadUrl, IProgress<double>? progress = null)
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "PhotoBookRenamer", "Update");
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
                Directory.CreateDirectory(tempDir);

                // Определяем расширение файла из URL
                var isExe = downloadUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                var extension = isExe ? ".exe" : ".zip";
                var filePath = Path.Combine(tempDir, $"update{extension}");
                
                // Загружаем файл
                using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? 0L;
                    var downloadedBytes = 0L;

                    using (var fileStream = new FileStream(filePath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    {
                        var buffer = new byte[8192];
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            downloadedBytes += bytesRead;
                            
                            if (totalBytes > 0 && progress != null)
                            {
                                var percent = (double)downloadedBytes / totalBytes * 100;
                                progress.Report(percent);
                            }
                        }
                    }
                }

                // Если это EXE установщик - запускаем напрямую
                if (isExe)
                {
                    var processInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true,
                        Verb = "runas" // Запуск от имени администратора
                    };

                    System.Diagnostics.Process.Start(processInfo);
                    
                    // Закрываем текущее приложение
                    await Task.Delay(1000);
                    System.Windows.Application.Current.Shutdown();
                    
                    return true;
                }
                
                // Если это ZIP - распаковываем и запускаем install.bat
                var extractPath = Path.Combine(tempDir, "extracted");
                Directory.CreateDirectory(extractPath);
                ZipFile.ExtractToDirectory(filePath, extractPath);

                // Запускаем установщик
                var installerPath = Path.Combine(extractPath, "install.bat");
                if (File.Exists(installerPath))
                {
                    var processInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = installerPath,
                        WorkingDirectory = extractPath,
                        UseShellExecute = true,
                        Verb = "runas" // Запуск от имени администратора
                    };

                    System.Diagnostics.Process.Start(processInfo);
                    
                    // Закрываем текущее приложение
                    await Task.Delay(1000);
                    System.Windows.Application.Current.Shutdown();
                    
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при загрузке обновления: {ex.Message}");
                return false;
            }
        }
    }
}





