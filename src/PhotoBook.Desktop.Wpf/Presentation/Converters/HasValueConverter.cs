using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PhotoBook.Core;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBookRenamer.Presentation.Converters
{
    /// <summary>
    /// Non-null / non-empty -> true. Drives filled vs empty slot visuals.
    /// Returns a <see cref="Visibility"/> when the target is a Visibility property.
    /// </summary>
    public class HasValueConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool result;
            if (value is string s) result = !string.IsNullOrWhiteSpace(s);
            else if (value is System.Collections.ICollection c) result = c.Count > 0;
            else result = value != null;

            return targetType == typeof(Visibility)
                ? (result ? Visibility.Visible : Visibility.Collapsed)
                : result;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
