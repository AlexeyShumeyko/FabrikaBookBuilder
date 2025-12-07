using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PhotoBookRenamer.Utils
{
    public class OrientationToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Orientation orientation && parameter is string param)
            {
                if (param == "Vertical" && orientation == Orientation.Vertical)
                    return Visibility.Visible;
                if (param == "Horizontal" && orientation == Orientation.Horizontal)
                    return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}



