using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using PhotoBookRenamer.Models;

namespace PhotoBookRenamer.Utils
{
    public class IsBookFilledConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Book book)
            {
                // Проверяем, что обложка заполнена
                if (book.Cover == null || string.IsNullOrEmpty(book.Cover.SourcePath))
                {
                    return false;
                }
                
                // Проверяем, что все страницы заполнены
                var pagesWithoutCover = book.Pages.Where(p => !p.IsCover).ToList();
                if (pagesWithoutCover.Count == 0)
                {
                    return false;
                }
                
                return pagesWithoutCover.All(p => !string.IsNullOrEmpty(p.SourcePath));
            }
            
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

