using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PhotoBookRenamer.Infrastructure
{
    public class FileService : IFileService
    {
        private static readonly string[] JpegExtensions = { ".jpg", ".jpeg", ".JPG", ".JPEG" };

        public async Task<List<string>> GetJpegFilesAsync(string folderPath)
        {
            return await Task.Run(() =>
            {
                if (!Directory.Exists(folderPath))
                {
                    return new List<string>();
                }

                var allFiles = Directory.GetFiles(folderPath);
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

                // Проверяем каждую папку
                foreach (var folder in folderPaths)
                {
                    var folderName = Path.GetFileName(folder);
                    
                    if (!Directory.Exists(folder))
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

                    // Проверяем, что все файлы - JPG (используем уже полученный список)
                    var allFiles = Directory.GetFiles(folder);
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

                // Проверяем, что все папки содержат одинаковое количество файлов
                // Используем количество файлов, которое встречается у большинства папок
                if (folderFileCounts.Count > 1)
                {
                    // Группируем по количеству файлов и находим группу с наибольшим количеством папок
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
                        var folderName = Path.GetFileName(problemFolder.Key);
                        var expectedCount = majorityCount;
                        var actualCount = problemFolder.Value;
                        
                        var errorMessage = $"❌ Количество файлов в папке \"{folderName}\" не совпадает с большинством папок.\n\n" +
                                          $"Ожидается: {expectedCount} файлов (как у {majorityFolders.Count} из {folderFileCounts.Count} папок)\n" +
                                          $"Найдено: {actualCount} файлов\n\n" +
                                          $"Удалите проблемную папку из списка и попробуйте снова.";
                        
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = errorMessage,
                            ProblemFolder = problemFolder.Key
                        };
                    }
                }

                return new ValidationResult { IsValid = true };
            });
        }

        public Task<string?> SelectFoldersAsync()
        {
            return Task.Run(() =>
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Выберите папки с фотографиями",
                    UseDescriptionForTitle = true
                };

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    return dialog.SelectedPath;
                }

                return null;
            });
        }

        public Task<string?> SelectOutputFolderAsync(string? defaultPath = null)
        {
            return Task.Run(() =>
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Выберите папку для сохранения",
                    SelectedPath = defaultPath ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                };

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    return dialog.SelectedPath;
                }

                return null;
            });
        }

        public Task<string?> SelectOutputFolderWithNameAsync(string? defaultPath = null, string? defaultFolderName = null)
        {
            return System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new Presentation.Dialogs.FolderNameDialog(
                    defaultPath ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    defaultFolderName);
                dialog.Owner = System.Windows.Application.Current.MainWindow;
                
                if (dialog.ShowDialog() == true && dialog.SelectedPath != null && dialog.FolderName != null)
                {
                    var fullPath = Path.Combine(dialog.SelectedPath, dialog.FolderName);
                    return fullPath;
                }

                return null;
            }).Task;
        }

        public Task<string[]?> SelectFilesAsync()
        {
            return Task.Run(() =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "JPEG файлы|*.jpg;*.jpeg;*.JPG;*.JPEG",
                    Multiselect = true,
                    Title = "Выберите JPEG файлы"
                };

                if (dialog.ShowDialog() == true)
                {
                    return dialog.FileNames;
                }

                return null;
            });
        }

        public async Task CopyFileAsync(string source, string destination)
        {
            await Task.Run(() =>
            {
                var destDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                File.Copy(source, destination, overwrite: true);
            });
        }

        public bool IsJpegFile(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            return JpegExtensions.Contains(ext);
        }
    }
}





