using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using PhotoBookRenamer.Domain;

namespace PhotoBookRenamer.Presentation.Converters
{
    // КРИТИЧЕСКИ ВАЖНО: Новый конвертер, который получает Page напрямую
    // Это исключает проблемы с неправильным DataContext в MultiBinding
    public class PageSourceConverter : IValueConverter
    {
        // Кэш для BitmapImage - критически важно для производительности
        private static readonly Dictionary<string, BitmapImage> _imageCache = new();
        private static readonly object _cacheLock = new();

        /// <summary>
        /// Decoded images that could not be shown, with the reason. A cell that silently
        /// shows nothing is indistinguishable from an empty slot, and that is how "the
        /// photos fell off on the first open" stayed a mystery: the failures were swallowed
        /// here, in a converter, with nowhere to look afterwards.
        /// </summary>
        private static readonly string ErrorLogPath = Path.Combine(
            Path.GetTempPath(), "PhotoBookRenamer", "image-errors.log");

        // Метод для очистки кэша конкретного файла.
        public static void ClearCacheForFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            lock (_cacheLock)
            {
                _imageCache.Remove(filePath);
            }
        }

        // Метод для очистки всего кэша
        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _imageCache.Clear();
            }
        }

        /// <summary>
        /// Decodes the photos of a project on a background thread and puts them in the same
        /// cache this converter reads, so the first render of a slot does not have to decode
        /// an 18 MB print file on the UI thread.
        ///
        /// That is not a micro-optimisation. The постраничка files are portrait scans of
        /// 18-19 MB; decoding four or five of them inside the layout pass freezes the window
        /// for seconds, and a slot whose decode has not produced a bitmap yet paints as an
        /// empty grey box - which is exactly what the owner reported as "the photo cells fell
        /// off on the first open, and a save plus reopen brought them back".
        ///
        /// Nothing here can make a render worse: a file that fails to decode is simply left
        /// out of the cache, and the converter falls back to reading it itself as before.
        /// </summary>
        public static async Task PrewarmAsync(IEnumerable<string>? filePaths)
        {
            var list = filePaths?.Where(p => !string.IsNullOrEmpty(p))
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .ToList() ?? new List<string>();
            if (list.Count == 0) return;

            await Task.Run(() =>
            {
                foreach (var path in list)
                {
                    try { Load(path); }
                    catch { /* recorded inside Load */ }
                }
            });
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // КРИТИЧЕСКИ ВАЖНО: Поддерживаем как Page объект, так и string (SourcePath)
            // Это обеспечивает обратную совместимость
            string? imagePath = null;
            string? thumbnailPath = null;

            if (value == null)
            {
                return null;
            }

            if (value is Page page)
            {
                // Если передан объект Page, используем ThumbnailPath если он есть
                thumbnailPath = page.ThumbnailPath;
                imagePath = page.SourcePath;
            }
            else if (value is string path)
            {
                // Если передан string, используем его как SourcePath
                imagePath = path;
            }
            else
            {
                // Неизвестный тип - это может быть нормально для некоторых биндингов
                return null;
            }

            // Пустой SourcePath - это нормально для пустых ячеек
            if (string.IsNullOrEmpty(imagePath))
            {
                return null;
            }

            var bitmap = Load(imagePath!, thumbnailPath);
            if (bitmap != null)
            {
                (value as Page)?.MarkImageOk();
                return bitmap;
            }

            (value as Page)?.MarkImageFailed(imagePath!);
            return null;
        }

        /// <summary>
        /// The bitmap for a photo: its thumbnail when there is one, otherwise the original
        /// scaled down. Cached by whichever source was actually read, so a repeat render is
        /// free and a later thumbnail never re-decodes the original.
        /// </summary>
        private static BitmapImage? Load(string imagePath, string? thumbnailPath = null)
        {
            // КРИТИЧЕСКИ ВАЖНО: Сначала пытаемся использовать миниатюру, если она есть
            // Это значительно ускоряет загрузку и снижает потребление памяти
            if (!string.IsNullOrEmpty(thumbnailPath) && File.Exists(thumbnailPath))
            {
                var fromThumb = TryDecode(thumbnailPath, $"thumb_{thumbnailPath}", null);
                if (fromThumb != null) return fromThumb;
            }

            // Если миниатюра не найдена или не загрузилась, загружаем полное изображение
            if (!File.Exists(imagePath))
            {
                Log(imagePath, "file not found");
                return null;
            }

            return TryDecode(imagePath, imagePath, 600);
        }

        private static BitmapImage? TryDecode(string source, string cacheKey, int? decodeWidth)
        {
            lock (_cacheLock)
            {
                if (_imageCache.TryGetValue(cacheKey, out var cached)) return cached;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.None;
                // КРИТИЧЕСКИ ВАЖНО: размер декодируемого изображения ограничен, но задаётся
                // ТОЛЬКО DecodePixelWidth. Если задать обе стороны, WPF растягивает картинку
                // ровно в эти размеры и пропорции теряются: фото выглядит вытянутым и
                // искажённым. С одной стороной вторая считается по пропорциям оригинала.
                // 600px хватает с запасом для карточки 190px высотой.
                if (decodeWidth.HasValue) bitmap.DecodePixelWidth = decodeWidth.Value;
                bitmap.UriSource = new Uri(source, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                lock (_cacheLock)
                {
                    _imageCache[cacheKey] = bitmap;
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                Log(source, ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static void Log(string path, string reason)
        {
            try
            {
                var dir = Path.GetDirectoryName(ErrorLogPath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(ErrorLogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {path}  ->  {reason}{Environment.NewLine}");
            }
            catch
            {
                // A log that cannot be written is not worth failing a render over.
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
