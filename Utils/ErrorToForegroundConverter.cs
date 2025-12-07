using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PhotoBookRenamer.Utils
{
    public class ErrorToForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool hasError && hasError)
            {
                return new SolidColorBrush(Color.FromRgb(211, 47, 47)); // #D32F2F красный
            }
            return new SolidColorBrush(Color.FromRgb(74, 74, 74)); // #4A4A4A темно-серый
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}












