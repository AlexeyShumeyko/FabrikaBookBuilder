using System;
using System.Globalization;
using System.Windows.Data;

namespace PhotoBookRenamer.Utils
{
    public class MultiplyConverter : IValueConverter
    {
        public int Multiplier { get; set; } = 2;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue)
            {
                return intValue * Multiplier;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}




