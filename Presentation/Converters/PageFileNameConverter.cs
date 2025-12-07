using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using PhotoBookRenamer.Domain;

namespace PhotoBookRenamer.Presentation.Converters
{
    public class PageFileNameConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length != 3) return string.Empty;
            
            if (values[0] is Page cover && 
                values[1] is System.Collections.ObjectModel.ObservableCollection<Page> pages && 
                values[2] is int slotIndex)
            {
                // Если индекс 0 - возвращаем имя файла обложки
                if (slotIndex == 0)
                {
                    if (!string.IsNullOrEmpty(cover.SourcePath))
                    {
                        try
                        {
                            return System.IO.Path.GetFileName(cover.SourcePath);
                        }
                        catch
                        {
                            return string.Empty;
                        }
                    }
                    return string.Empty;
                }
                else
                {
                    // Иначе возвращаем имя файла страницы по индексу
                    // slotIndex - это номер разворота (1, 2, 3, 4...)
                    var pagesWithoutCover = pages.Where(p => !p.IsCover).ToList();
                    var pageIndex = slotIndex - 1; // slotIndex начинается с 1, индекс с 0
                    
                    if (pageIndex >= 0 && pageIndex < pagesWithoutCover.Count)
                    {
                        var page = pagesWithoutCover[pageIndex];
                        if (!string.IsNullOrEmpty(page.SourcePath))
                        {
                            try
                            {
                                return System.IO.Path.GetFileName(page.SourcePath);
                            }
                            catch
                            {
                                return string.Empty;
                            }
                        }
                    }
                }
            }
            
            return string.Empty;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}






