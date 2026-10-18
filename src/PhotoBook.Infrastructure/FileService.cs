using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PhotoBook.Application;

namespace PhotoBook.Infrastructure
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
    internal class FileService : IFileService
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
                            ErrorMessage = $"Папка «{folderName}» не найдена. Проверьте, не была ли она перемещена или удалена.",
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
                            ErrorMessage = $"В папке «{folderName}» есть файлы, отличные от фотографий. Программа обрабатывает только файлы JPG и JPEG. Уберите эти файлы из папки или выберите другую папку.",
                            ProblemFolder = folder
                        };
                    }

                    if (files.Count == 0)
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"В папке «{folderName}» нет фотографий. Выберите папку, содержащую файлы JPG или JPEG.",
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
                    var problemFolders = folderFileCounts
                        .Where(kvp => kvp.Value != majorityCount)
                        .ToList();

                    if (problemFolders.Any())
                    {
                        var problemFolder = problemFolders.First();
                        var folderName = System.IO.Path.GetFileName(problemFolder.Key);

                        // Three lines, in the order they are needed: what was found,
                        // the rule that makes it a problem, and what to do about it. The
                        // version before this was written for whoever wrote it - "expected 8
                        // (as in 1 of 2 folders)" answers a question nobody asked.
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage =
                                $"Количество файлов в папке «{folderName}» - {problemFolder.Value} и не совпадает с большинством выбранных папок." +
                                Environment.NewLine +
                                "Один проект собирается из папок только с одинаковым количеством файлов." +
                                Environment.NewLine +
                                "Удалите проблемную папку из списка",
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

        /// <summary>
        /// Asked by the application layer to tell a finished run from one whose output folder
        /// has since been deleted. A folder that cannot be reached counts as not there: the
        /// honest answer for a project is "not finished", not an exception at a menu.
        /// </summary>
        public bool DirectoryExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            try
            {
                return System.IO.Directory.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc />
        public bool FileExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return System.IO.File.Exists(path);
        }

        /// <summary>
        /// Creating a folder can fail for reasons the caller can do nothing about - no
        /// permission, a path that is a file, a disk that is full. The failure is reported
        /// when the first file is copied into it, with the folder's own name in the message,
        /// which is more use than an exception about the folder would be.
        /// </summary>
        public void EnsureDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || System.IO.Directory.Exists(path)) return;

            try
            {
                System.IO.Directory.CreateDirectory(path);
            }
            catch (Exception)
            {
                // Deliberately swallowed. See above.
            }
        }
    }
}
