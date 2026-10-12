using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using PhotoBook.Core;

namespace PhotoBookRenamer.Presentation.Converters
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
                var index = pageNumber - 1; // the page number starts at 1, the index at 0

                if (index >= 0 && index < pagesWithoutCover.Count)
                {
                    var page = pagesWithoutCover[index];
                    string? imagePath = null;

                    // The preview when there is one, the original otherwise. The second
                    // is slower and heavier, but an empty frame tells the photographer
                    // nothing about whether the slot is filled.
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
                            // CacheOption.OnLoad reads and closes the file, which is what
                            // makes the bitmap freezable and therefore shareable between threads.
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.UriSource = new Uri(imagePath);
                            bitmap.EndInit();
                            bitmap.Freeze(); // frozen, so a background thread may hand it to the UI thread
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


