using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using PhotoBook.Shell.Services;

namespace PhotoBook.Shell.Converters
{
    /// <summary>
    /// A file path to the decoded image behind it, decoded at the size it will be drawn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The converter parameter is the width in pixels, and it is not optional in practice: a
    /// binding that leaves it out asks for the whole photograph to render into a 48-pixel
    /// tile. That was the first version's behaviour and it is the reason the file list froze.
    /// </para>
    /// <para>
    /// The cache is resolved once and held statically, because a binding on a card can fire
    /// many times and asking the container each time would make the decode the most expensive
    /// thing in the row rather than the cheapest. <see cref="Attach"/> is called once at
    /// startup, before any card exists.
    /// </para>
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
        {
            if (value is not string path) return null;

            // A small picture is asked for here - every caller of this converter draws into a
            // thumbnail-sized box - so the thumbnail is drawn when there is one. The original
            // is still what the record names, and still what gets opened, exported and
            // measured from; this only decides which of the two is decoded to fill a box.
            //
            // "original" as the parameter asks for the whole file, for the rare caller that
            // means it.
            var wantsOriginal = (parameter as string)?.Equals("original", StringComparison.OrdinalIgnoreCase) == true;
            var file = wantsOriginal ? path : PreviewFile.PreferThumbnail(path);

            return _cache?.Get(file, Width(parameter));
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();

        /// <summary>For a preview tile: never null, so the border and corner radius still apply.</summary>
        public Bitmap? GetOrNull(string? path) => path is null ? null : _cache?.Get(path);

        private static int Width(object? parameter) =>
            int.TryParse(parameter?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
                ? BitmapScale.Pixels(width)
                : 0;
    }
}
