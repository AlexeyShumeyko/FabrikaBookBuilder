using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using PhotoBookRenamer.Models;

namespace PhotoBookRenamer.Utils
{
    public class PageSourcePathConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length != 2) return string.Empty;
            
            if (values[0] is System.Collections.ObjectModel.ObservableCollection<Page> pages && 
                values[1] is int pageNumber)
            {
                var pagesWithoutCover = pages.Where(p => !p.IsCover).ToList();
                var index = pageNumber - 1;
                
                if (index >= 0 && index < pagesWithoutCover.Count)
                {
                    return pagesWithoutCover[index]?.SourcePath ?? string.Empty;
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












