using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using System.Collections.Generic;
using System.Threading.Tasks;

namespace PhotoBookRenamer.Infrastructure
{
    /// <summary>
    /// Reading and writing files, and nothing else.
    ///
    /// <para>
    /// This class used to open three windows: a folder browser twice, and a folder dialog with
    /// a typed name. None of the three had a caller, and all three pulled Windows Forms and
    /// the main window into the file layer. Choosing where photographs come from is a
    /// question for a person, so it now lives in the shell behind
    /// <see cref="PhotoBook.Application.IPickFiles"/>.
    /// </para>
    /// </summary>
    public class FileService : IFileService
    {
        private static readonly string[] JpegExtensions = { ".jpg", ".jpeg", ".JPG", ".JPEG" };

        public Task<List<string>> GetJpegFilesAsync(string folderPath)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrEmpty(folderPath) || !System.IO.Directory.Exists(folderPath))
                {
                    return new List<string>();
                }

                var allFiles = System.IO.Directory.GetFiles(folderPath);
                var jpegFiles = allFiles.Where(IsJpegFile).OrderBy(f => f).ToList();

                return jpegFiles;
            });
        }

        public async Task<bool> ValidateFoldersAsync(List<string> folderPaths)
        {
            var result = await ValidateFoldersDetailedAsync(folderPaths);
            return result.IsValid;
        }

        public async Task<ValidationResult> ValidateFoldersDetailedAsync(List<string> folderPaths)
        {
            return await Task.Run(async () =>
            {
                if (folderPaths == null || folderPaths.Count == 0)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Не выбрано ни одной папки."
                    };
                }

                var folderFileCounts = new Dictionary<string, int>();

                foreach (var folder in folderPaths)
                {
                    var folderName = System.IO.Path.GetFileName(folder);

                    if (!System.IO.Directory.Exists(folder))
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"Папка не существует: {folderName}",
                            ProblemFolder = folder
                        };
                    }

                    var files = await GetJpegFilesAsync(folder);
                    folderFileCounts[folder] = files.Count;

                    var allFiles = System.IO.Directory.GetFiles(folder);
                    var nonJpegFiles = allFiles.Where(f => !IsJpegFile(f)).ToList();

                    if (nonJpegFiles.Any())
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"В папке {folderName} найдены файлы не в формате JPG/JPEG.",
                            ProblemFolder = folder
                        };
                    }

                    if (files.Count == 0)
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"В папке {folderName} не найдено JPG/JPEG файлов.",
                            ProblemFolder = folder
                        };
                    }
                }

                if (folderFileCounts.Count > 1)
                {
                    var groupsByCount = folderFileCounts
                        .GroupBy(kvp => kvp.Value)
                        .OrderByDescending(g => g.Count())
                        .ToList();

                    var majorityCount = groupsByCount.First().Key;
                    var majorityFolders = groupsByCount.First().ToList();
                    var problemFolders = folderFileCounts
                        .Where(kvp => kvp.Value != majorityCount)
                        .ToList();

                    if (problemFolders.Any())
                    {
                        var problemFolder = problemFolders.First();
                        var folderName = System.IO.Path.GetFileName(problemFolder.Key);

                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage =
                                $"❌ Количество файлов в папке \"{folderName}\" не совпадает с большинством папок.\n\n" +
                                $"Ожидается: {majorityCount} файлов (как у {majorityFolders.Count} из {folderFileCounts.Count} папок)\n" +
                                $"Найдено: {problemFolder.Value} файлов\n\n" +
                                $"Удалите проблемную папку из списка и попробуйте снова.",
                            ProblemFolder = problemFolder.Key
                        };
                    }
                }

                return new ValidationResult { IsValid = true };
            });
        }

        public async Task CopyFileAsync(string source, string destination)
        {
            await Task.Run(() =>
            {
                var destDir = System.IO.Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(destDir) && !System.IO.Directory.Exists(destDir))
                {
                    System.IO.Directory.CreateDirectory(destDir);
                }

                System.IO.File.Copy(source, destination, overwrite: true);
            });
        }

        public bool IsJpegFile(string filePath)
        {
            var ext = System.IO.Path.GetExtension(filePath);
            return JpegExtensions.Contains(ext);
        }
    }
}
