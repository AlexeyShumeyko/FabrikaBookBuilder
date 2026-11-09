using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PhotoBook.Core;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Shell.Converters
{
    /// <summary>
    /// The file list's state badge: one solid colour per state, white text on it, no frame.
    /// </summary>
    /// <remarks>
    /// Assigned is the green of "used", run-wide is the product's own colour because it is
    /// the one state that is the same in every book, and free is the faint grey of a state
    /// that needs no attention. Painted like a button, which is what it is.
    /// </remarks>
    public class PhotoUsageToBrushConverter : IValueConverter
    {
        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => value switch
            {
                PhotoUsage.Assigned => Solid("#10B981"),
                PhotoUsage.Shared => Solid("#6366F1"),
                _ => Solid("#94A3B8")
            };

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();

        private static IBrush Solid(string hex) => new SolidColorBrush(Color.Parse(hex));
    }
}
