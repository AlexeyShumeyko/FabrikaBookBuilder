using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using PhotoBookRenamer.Domain;

namespace PhotoBookRenamer.Presentation.Converters
{
    public class HelpSectionToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is HelpSection currentSection && parameter is string paramString)
            {
                if (Enum.TryParse<HelpSection>(paramString, out var targetSection))
                {
                    return currentSection == targetSection ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


