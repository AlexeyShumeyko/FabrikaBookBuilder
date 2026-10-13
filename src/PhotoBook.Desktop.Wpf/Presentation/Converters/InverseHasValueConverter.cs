using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PhotoBook.Core;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBookRenamer.Presentation.Converters
{
    /// <summary>Inverse of <see cref="HasValueConverter"/>.</summary>
    public class InverseHasValueConverter : IValueConverter
    {
        private static readonly HasValueConverter Inner = new HasValueConverter();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool result = (bool)Inner.Convert(value, typeof(bool), parameter, culture);
            return targetType == typeof(Visibility)
                ? (result ? Visibility.Collapsed : Visibility.Visible)
                : !result;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
