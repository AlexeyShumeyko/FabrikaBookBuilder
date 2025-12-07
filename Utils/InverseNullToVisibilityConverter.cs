using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PhotoBookRenamer.Utils
{
    public class InverseNullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return Visibility.Visible;
            
            if (value is int intValue)
                return intValue == 0 ? Visibility.Visible : Visibility.Collapsed;
            
            if (value is string strValue)
                return string.IsNullOrEmpty(strValue) ? Visibility.Visible : Visibility.Collapsed;
            
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}












