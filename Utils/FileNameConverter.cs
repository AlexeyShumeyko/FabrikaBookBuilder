using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace PhotoBookRenamer.Utils
{
    public class FileNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string filePath && !string.IsNullOrEmpty(filePath))
            {
                try
                {
                    return Path.GetFileName(filePath);
                }
                catch
                {
                    return filePath;
                }
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}







