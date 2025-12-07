using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PhotoBookRenamer.Utils
{
    public class ErrorToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool hasError && hasError)
            {
                // Более заметный красный фон
                return new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(255, 235, 235)); // #FFEBEB - светло-красный
            }
            return System.Windows.Media.Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

