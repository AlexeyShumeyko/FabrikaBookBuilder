using System;
using System.Globalization;
using System.Windows.Data;
using PhotoBookRenamer.Models;

namespace PhotoBookRenamer.Utils
{
    public class IsPageFilledConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Page page)
            {
                return !string.IsNullOrEmpty(page.SourcePath);
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

