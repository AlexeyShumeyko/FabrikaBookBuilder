using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using PhotoBookRenamer.Models;

namespace PhotoBookRenamer.Utils
{
    public class PageByIndexConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length != 2) return null;
            
            if (values[0] is System.Collections.ObjectModel.ObservableCollection<Page> pages && 
                values[1] is int pageNumber)
            {
                var pagesWithoutCover = pages.Where(p => !p.IsCover).ToList();
                var index = pageNumber - 1; // pageNumber начинается с 1, индекс с 0
                
                if (index >= 0 && index < pagesWithoutCover.Count)
                {
                    var page = pagesWithoutCover[index];
                    string? imagePath = null;
                    
                    // Используем ThumbnailPath, если он есть, иначе SourcePath
                    // КРИТИЧЕСКИ ВАЖНО: Если миниатюры нет, используем SourcePath
                    // Это временно загрузит полное изображение, но миниатюра будет создана при необходимости
                    if (!string.IsNullOrEmpty(page.ThumbnailPath) && System.IO.File.Exists(page.ThumbnailPath))
                    {
                        imagePath = page.ThumbnailPath;
                    }
                    else if (!string.IsNullOrEmpty(page.SourcePath) && System.IO.File.Exists(page.SourcePath))
                    {
                        imagePath = page.SourcePath;
                    }
                    
                    if (!string.IsNullOrEmpty(imagePath))
                    {
                        try
                        {
                            // Создаем новый BitmapImage с кэшированием отключенным для обновления
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.UriSource = new Uri(imagePath);
                            bitmap.EndInit();
                            bitmap.Freeze(); // Замораживаем для потокобезопасности
                            return bitmap;
                        }
                        catch
                        {
                            return null;
                        }
                    }
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


