using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PhotoBook.Core;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBookRenamer.Presentation.Converters
{
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool has = value is string s ? !string.IsNullOrWhiteSpace(s)
                     : value is System.Collections.ICollection c ? c.Count > 0
                     : value != null;
            return has ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
