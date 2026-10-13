using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using PhotoBook.Shell.Services;

namespace PhotoBook.Shell.Converters
{
    /// <summary>
    /// A file path to the decoded image behind it.
    /// </summary>
    /// <remarks>
    /// The cache is resolved once and held statically, because a binding on a card can fire
    /// many times and asking the container each time would make the decode the most expensive
    /// thing in the row rather than the cheapest. <see cref="Attach"/> is called once at
    /// startup, before any card exists.
    /// </remarks>
    public class FilePathToBitmapConverter : IValueConverter
    {
        private static AvaloniaImageCache? _cache;

        public static void Attach(IServiceProvider services)
            => _cache = services.GetRequiredService<AvaloniaImageCache>();

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => value is string path ? _cache?.Get(path) : null;

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();

        /// <summary>For a preview tile: never null, so the border and corner radius still apply.</summary>
        public Bitmap? GetOrNull(string? path) => path is null ? null : _cache?.Get(path);
    }
}