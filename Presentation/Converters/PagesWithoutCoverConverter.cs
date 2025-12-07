using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using PhotoBookRenamer.Domain;

namespace PhotoBookRenamer.Presentation.Converters
{
    public class PagesWithoutCoverConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is IEnumerable<Page> pages)
            {
                return pages.Where(p => !p.IsCover).ToList();
            }
            if (value is IEnumerable enumerable)
            {
                return enumerable.Cast<Page>().Where(p => !p.IsCover).ToList();
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}













