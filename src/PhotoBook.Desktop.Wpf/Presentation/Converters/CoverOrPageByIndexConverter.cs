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

        public static void ClearCacheForPage(int bookIndex, int pageIndex)
        {
            var key = $"{bookIndex}_{pageIndex}";
            lock (_cacheLock)
            {
                _imageCache.Remove(key);
            }
        }

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

                if (slotIndex == 0)
                {
                    targetPage = cover;
                }
                else
                {
                    // Matched by Page.Index rather than by position: the number here is the
                    // slot the photographer sees, not where the page sits in the book.
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

                    lock (_cacheLock)
                    {
                        if (_imageCache.TryGetValue(thumbCacheKey, out var cachedThumbnail))
                        {
                            return cachedThumbnail;
                        }
                    }

                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.CreateOptions = BitmapCreateOptions.None;
                        bitmap.UriSource = new Uri(thumbnailPath, UriKind.Absolute);
                        bitmap.EndInit();
                        bitmap.Freeze();

                        lock (_cacheLock)
                        {
                            _imageCache[thumbCacheKey] = bitmap;
                        }

                        return bitmap;
                    }
                    catch
                    {
                    }
                }

                // The cache is only good while the page still points at the same
                // photograph. A slot that was reassigned would otherwise keep showing
                // the bitmap of whatever used to stand there.
                lock (_cacheLock)
                {
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

                    if (_imageCache.TryGetValue(cacheKey, out var cachedBitmap))
                    {
                        if (targetPage.SourcePath == imagePath)
                        {
                            return cachedBitmap;
                        }
                    }
                }

                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.None;
                    bitmap.DecodePixelWidth = 600;
                    // DecodePixelWidth alone: giving both sides a size would stretch the photograph,
                    // while the second side follows the proportions of the original.
                    bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();

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



