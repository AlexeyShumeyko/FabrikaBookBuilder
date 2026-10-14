using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using PhotoBook.Core;
using PhotoBook.Shell.Services;

namespace PhotoBook.Shell.Converters
{
    /// <summary>
    /// A page, as the picture to draw for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is one converter doing three things that have to happen together, and splitting
    /// them would be worse than the length:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Pick the cheap file.</b> The reduced copy when it exists, the original
    /// otherwise. A cell that opens an original is what cost 12.3 seconds and 824 MB, and it
    /// cost it inside the layout pass - which is the second and a half in which the window
    /// stops answering at the moment a project opens.</item>
    /// <item><b>Scale to the monitor.</b> Decoding is in device pixels; the cell is measured
    /// in layout units. Without this a photograph decoded for a 100-percent screen is
    /// stretched back up on a denser one, and everything goes soft.</item>
    /// <item><b>Ask the cache once.</b> The cache is resolved statically at start-up, so a
    /// binding that re-evaluates does not decode again.</item>
    /// </list>
    /// </remarks>
    public class PageToBitmapConverter : IValueConverter
    {
        private static AvaloniaImageCache? _cache;

        public static void Attach(IServiceProvider services)
            => _cache = services.GetRequiredService<AvaloniaImageCache>();

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            if (value is not Page page || _cache is null) return null;

            var path = !string.IsNullOrEmpty(page.ThumbnailPath)
                ? page.ThumbnailPath
                : page.SourcePath;

            if (string.IsNullOrEmpty(path)) return null;

            return _cache.Get(path, BitmapScale.Pixels(Width(parameter)));
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();

        private static int Width(object? parameter) =>
            int.TryParse(parameter?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
                ? width
                : 0;
    }
}
