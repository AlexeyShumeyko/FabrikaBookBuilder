using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using PhotoBookRenamer.Models;

namespace PhotoBookRenamer.Utils
{
    // КРИТИЧЕСКИ ВАЖНО: Новый конвертер, который получает Page напрямую
    // Это исключает проблемы с неправильным DataContext в MultiBinding
    public class PageSourceConverter : IValueConverter
    {
        // Кэш для BitmapImage - критически важно для производительности
        private static readonly System.Collections.Generic.Dictionary<string, BitmapImage> _imageCache = new();
        private static readonly object _cacheLock = new();
        
        // Метод для очистки кэша конкретного файла
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
            
            if (string.IsNullOrEmpty(imagePath))
            {
                // Пустой SourcePath - это нормально для пустых ячеек
                return null;
            }
            
            // КРИТИЧЕСКИ ВАЖНО: Сначала пытаемся использовать миниатюру, если она есть
            // Это значительно ускоряет загрузку и снижает потребление памяти
            if (!string.IsNullOrEmpty(thumbnailPath) && File.Exists(thumbnailPath))
            {
                var cacheKey = $"thumb_{thumbnailPath}";
                
                // Проверяем кэш для миниатюры
                lock (_cacheLock)
                {
                    if (_imageCache.TryGetValue(cacheKey, out var cachedThumbnail))
                    {
                        return cachedThumbnail;
                    }
                }
                
                // Загружаем миниатюру
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.None;
                    bitmap.UriSource = new Uri(thumbnailPath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    
                    // Сохраняем в кэш
                    lock (_cacheLock)
                    {
                        _imageCache[cacheKey] = bitmap;
                    }
                    
                    return bitmap;
                }
                catch
                {
                    // Продолжаем загрузку полного изображения
                }
            }
            
            // Если миниатюра не найдена или не загрузилась, загружаем полное изображение
            if (!File.Exists(imagePath))
            {
                return null;
            }
            
            // Проверяем кэш для полного изображения
            lock (_cacheLock)
            {
                if (_imageCache.TryGetValue(imagePath, out var cachedBitmap))
                {
                    return cachedBitmap;
                }
            }
            
            // Загружаем новое изображение
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.None;
                // КРИТИЧЕСКИ ВАЖНО: Ограничиваем размер декодируемого изображения для производительности
                // 600x900 достаточно для отображения в ячейках 220px высотой
                bitmap.DecodePixelWidth = 600;
                bitmap.DecodePixelHeight = 900;
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                
                // Сохраняем в кэш
                lock (_cacheLock)
                {
                    _imageCache[imagePath] = bitmap;
                }
                
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

