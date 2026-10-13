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
    /// A rounded <see cref="RectangleGeometry"/> for a <c>Geometry</c> property, built from
    /// the target's own ActualWidth / ActualHeight and a radius from the
    /// ConverterParameter.
    ///
    /// Why this exists: <c>Border.ClipToBounds</c> clips to the bounding RECTANGLE, not
    /// to the corner radius. A photo flush with a rounded card therefore paints its square
    /// corners over the rounded border. The card's whole content - photo and footer -
    /// needs one rounded clip, which also trims the hover zoom.
    ///
    /// Self-referencing ActualWidth is safe here: Clip does not take part in measure, so
    /// there is no layout cycle (unlike a value that measure depends on).
    /// </summary>
    public class RoundedClipGeometryConverter : IMultiValueConverter
    {
        public object? Convert(object?[]? values, Type targetType, object? parameter, CultureInfo culture)
        {
            double width = values != null && values.Length > 0 ? AsDouble(values[0]) : 0d;
            double height = values != null && values.Length > 1 ? AsDouble(values[1]) : 0d;

            // Nothing sensible to clip to yet: let the layout settle, then clip.
            if (width <= 0d || height <= 0d) return null;

            double radius = 12d;
            if (double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                radius = parsed;

            // A radius larger than half the shorter side renders as a malformed geometry.
            radius = Math.Min(radius, Math.Min(width, height) / 2d);

            return new System.Windows.Media.RectangleGeometry(
                new System.Windows.Rect(0, 0, width, height), radius, radius);
        }

        private static double AsDouble(object? o) => o switch
        {
            double d => d,
            int i => i,
            float f => f,
            _ => 0d
        };

        public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
            => Array.Empty<object?>();
    }
}
