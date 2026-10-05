using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using PhotoBook.Core;

namespace PhotoBookRenamer.Presentation.Converters
{
    public class CoverOrPageByIndexConverter : IMultiValueConverter
    {
        // Keyed by slot and page, never by file path: the same photograph may stand
        // in several slots at once, and a cache keyed by path would hand one slot's
        // copy to another.
        private static readonly Dictionary<string, BitmapImage> _imageCache = new();
        private static readonly object _cacheLock = new();

        // Метод для очистки кэша конкретной страницы
        public static void ClearCacheForPage(int bookIndex, int pageIndex)
        {
            var key = $"{bookIndex}_{pageIndex}";
            lock (_cacheLock)
            {
                _imageCache.Remove(key);
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

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length != 3)
                return null;

            if (values[0] is Page cover && values[1] is System.Collections.ObjectModel.ObservableCollection<Page> pages && values[2] is int slotIndex)
            {
                Page? targetPage = null;

                // Определяем целевую страницу
                if (slotIndex == 0)
                {
                    targetPage = cover;
                }
                else
                {
                    // Ищем страницу по Index (slotIndex соответствует Page.Index)
                    targetPage = pages.FirstOrDefault(p => !p.IsCover && p.Index == slotIndex);
                }

                if (targetPage == null || string.IsNullOrEmpty(targetPage.SourcePath) || !System.IO.File.Exists(targetPage.SourcePath))
                {
                    return null;
                }

                var imagePath = targetPage.SourcePath;
                var thumbnailPath = targetPage.ThumbnailPath;
                var cacheKey = $"{slotIndex}_{imagePath}";

                if (!string.IsNullOrEmpty(thumbnailPath) && System.IO.File.Exists(thumbnailPath))
                {
                    var thumbCacheKey = $"thumb_{slotIndex}_{thumbnailPath}";

                    // Проверяем кэш для миниатюры
                    lock (_cacheLock)
                    {
                        if (_imageCache.TryGetValue(thumbCacheKey, out var cachedThumbnail))
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
                            _imageCache[thumbCacheKey] = bitmap;
                        }

                        return bitmap;
                    }
                    catch
                    {
                        // Продолжаем загрузку полного изображения
                    }
                }

                // The cache is only good while the page still points at the same
                // photograph. A slot that was reassigned would otherwise keep showing
                // the bitmap of whatever used to stand there.
                lock (_cacheLock)
                {
                    // Удаляем старые записи кэша для этого slotIndex (если SourcePath изменился)
                    var keysToRemove = new List<string>();
                    foreach (var key in _imageCache.Keys)
                    {
                        if (key.StartsWith($"{slotIndex}_") && !key.EndsWith($"_{imagePath}"))
                        {
                            keysToRemove.Add(key);
                        }
                    }
                    foreach (var key in keysToRemove)
                    {
                        _imageCache.Remove(key);
                    }

                    // Проверяем кэш для текущего SourcePath
                    if (_imageCache.TryGetValue(cacheKey, out var cachedBitmap))
                    {
                        // Дополнительная проверка: убеждаемся, что SourcePath страницы все еще соответствует
                        if (targetPage.SourcePath == imagePath)
                        {
                            return cachedBitmap;
                        }
                    }
                }

                // Загружаем новое изображение
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.None;
                    bitmap.DecodePixelWidth = 600;
                    // Только DecodePixelWidth: обе стороны растянули бы фото,
                    // вторая считается по пропорциям оригинала.
                    bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
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
                    return null;
                }
            }

            return null;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}



