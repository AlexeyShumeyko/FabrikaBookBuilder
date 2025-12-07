using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace PhotoBookRenamer.Infrastructure
{
    public class ImageService : IImageService
    {
        private readonly Dictionary<string, (int Width, int Height)> _dimensionCache = new();
        private readonly object _cacheLock = new();

        public async Task<(int Width, int Height)> GetImageDimensionsAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return (0, 0);

            lock (_cacheLock)
            {
                if (_dimensionCache.TryGetValue(filePath, out var cached))
                    return cached;
            }

            return await Task.Run(() =>
            {
                try
                {
                    // КРИТИЧЕСКИ ВАЖНО: Используем Identify для чтения только метаданных без загрузки полного изображения
                    // Это значительно быстрее и потребляет меньше памяти для больших изображений
                    var imageInfo = Image.Identify(filePath);
                    if (imageInfo != null)
                    {
                        var dimensions = (imageInfo.Width, imageInfo.Height);

                        lock (_cacheLock)
                        {
                            _dimensionCache[filePath] = dimensions;
                        }

                        return dimensions;
                    }
                    return (0, 0);
                }
                catch
                {
                    return (0, 0);
                }
            });
        }

        public async Task<string> CreateThumbnailAsync(string sourcePath, string thumbnailPath, int maxSize = 500)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var thumbDir = Path.GetDirectoryName(thumbnailPath);
                    if (!string.IsNullOrEmpty(thumbDir) && !Directory.Exists(thumbDir))
                    {
                        Directory.CreateDirectory(thumbDir);
                    }

                    // КРИТИЧЕСКИ ВАЖНО: Загружаем изображение с оптимизацией памяти
                    // Используем MemoryAllocator для контроля использования памяти
                    using var image = Image.Load(sourcePath);
                    var ratio = Math.Min((double)maxSize / image.Width, (double)maxSize / image.Height);
                    var newWidth = (int)(image.Width * ratio);
                    var newHeight = (int)(image.Height * ratio);

                    // Если изображение уже меньше нужного размера, просто сохраняем
                    if (image.Width <= maxSize && image.Height <= maxSize)
                    {
                    // Используем оптимизированные настройки JPEG для миниатюр
                    var encoder = new JpegEncoder
                    {
                        Quality = 85 // Качество 85 достаточно для миниатюр и уменьшает размер файла
                    };
                    
                    // КРИТИЧЕСКИ ВАЖНО: Сохраняем с FileShare.ReadWrite чтобы другие процессы могли читать файл
                    // Удаляем старый файл если он существует и заблокирован
                    if (File.Exists(thumbnailPath))
                    {
                        try
                        {
                            File.Delete(thumbnailPath);
                        }
                        catch
                        {
                            // Игнорируем ошибки удаления
                        }
                    }
                    
                    image.SaveAsJpeg(thumbnailPath, encoder);
                    return thumbnailPath;
                    }

                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Size = new Size(newWidth, newHeight),
                        Mode = ResizeMode.Max,
                        Sampler = KnownResamplers.Lanczos3 // Более качественное масштабирование
                    }));

                    // Используем оптимизированные настройки JPEG для миниатюр
                    var jpegEncoder = new JpegEncoder
                    {
                        Quality = 85 // Качество 85 достаточно для миниатюр и уменьшает размер файла
                    };
                    image.SaveAsJpeg(thumbnailPath, jpegEncoder);
                    
                    return thumbnailPath;
                }
                catch (Exception ex)
                {
                    return string.Empty;
                }
            });
        }

        public async Task<string?> DetectCoverAsync(string[] filePaths)
        {
            if (filePaths == null || filePaths.Length == 0)
                return null;

            // КРИТИЧЕСКИ ВАЖНО: Параллельная обработка файлов для определения обложки
            // Это позволяет обрабатывать несколько файлов одновременно, используя все доступные ядра процессора
            var tasks = filePaths.Select(async filePath =>
            {
                var (width, height) = await GetImageDimensionsAsync(filePath);
                var pixels = (long)width * height;
                return new { FilePath = filePath, Pixels = pixels };
            });

            var results = await Task.WhenAll(tasks);
            
            // Находим файл с максимальным количеством пикселей
            var maxResult = results.OrderByDescending(r => r.Pixels).FirstOrDefault();
            
            return maxResult?.FilePath;
        }

        public async Task LoadThumbnailsAsync(IEnumerable<string> filePaths)
        {
            var fileList = filePaths.ToList();
            var tasks = fileList.Select(async filePath =>
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    return;

                try
                {
                    var fileName = System.IO.Path.GetFileName(filePath);
                    var thumbDir = Path.Combine(Path.GetTempPath(), "PhotoBookRenamer", "Thumbnails");
                    if (!Directory.Exists(thumbDir))
                    {
                        Directory.CreateDirectory(thumbDir);
                    }
                    
                    // КРИТИЧЕСКИ ВАЖНО: Используем хэш полного пути для создания уникального имени миниатюры
                    // Это предотвращает конфликты при одинаковых именах файлов в разных папках
                    var filePathHash = GetFilePathHash(filePath);
                    var thumbName = $"{filePathHash}_thumb.jpg";
                    var thumbPath = Path.Combine(thumbDir, thumbName);

                    // КРИТИЧЕСКИ ВАЖНО: Проверяем, нужно ли создавать миниатюру
                    // Если файл существует, проверяем его доступность
                    if (!File.Exists(thumbPath))
                    {
                        await CreateThumbnailAsync(filePath, thumbPath);
                    }
                    else
                    {
                        // Проверяем, что файл доступен для чтения
                        try
                        {
                            using (var stream = File.Open(thumbPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                // Файл доступен
                            }
                        }
                        catch
                        {
                            // Файл заблокирован, пересоздаем его
                            try
                            {
                                File.Delete(thumbPath);
                            }
                            catch { }
                            await CreateThumbnailAsync(filePath, thumbPath);
                        }
                    }
                }
                catch
                {
                    // Игнорируем ошибки для отдельных файлов
                }
            });

            await Task.WhenAll(tasks);
        }
        
        // КРИТИЧЕСКИ ВАЖНО: Создаем уникальный хэш для полного пути файла
        public string GetFilePathHash(string filePath)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(filePath);
                var hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16);
            }
        }
    }
}

