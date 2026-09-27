using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoBookRenamer.Presentation.Converters
{
    /// <summary>
    /// Turns a photo path into a bitmap for the 44-48px tiles: the thumbnail if one exists,
    /// otherwise a downscaled decode of the original.
    ///
    /// <para>
    /// This is the converter the whole "maximum speed" design rests on - the program is
    /// meant to touch only thumbnails, never the 18 MB originals - and it was doing the
    /// opposite of that on every single call:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item>a SHA-256 over the path string, allocated and recomputed on every call;</item>
    /// <item>a <c>File.Exists</c> on the UI thread, on every call;</item>
    /// <item>a fresh JPEG decode into a new BitmapImage, on every call, with nothing
    /// shared - so one photo shown in the pool and in a slot was decoded twice, and a row
    /// whose binding re-evaluated was decoded again.</item>
    /// </list>
    ///
    /// <para>
    /// With a hundred photos in the pool that is hundreds of hashes, hundreds of disk
    /// probes and hundreds of decodes during the first layout: 1.9 s measured, and it did
    /// not get a single millisecond cheaper on the second run, which is the signature of
    /// work being repeated instead of cached.
    /// </para>
    ///
    /// <para>
    /// Now every path is hashed once, probed once and decoded once, and the frozen
    /// bitmap is shared by every element that shows that photo. The cache is bounded and
    /// dropped whole when it grows past <see cref="MaxCached"/>, because the point of the
    /// exercise is a weak PC: a cache that grows without limit is just a slower leak.
    /// </para>
    /// </summary>
    public class FilePathToThumbnailConverter : IValueConverter
    {
        /// <summary>
        /// How many photos to keep decoded. A 48px tile is a few kilobytes decoded, so a
        /// few hundred is a couple of megabytes - and an order of two hundred is bigger
        /// than any real run this program assembles at once.
        /// </summary>
        private const int MaxCached = 400;

        private static readonly ConcurrentDictionary<string, BitmapSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> Hashes = new(StringComparer.OrdinalIgnoreCase);

        public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        {
            PhotoBookRenamer.Presentation.PerfPhase.Count("FilePathToThumbnailConverter");
            if (value is not string filePath || string.IsNullOrEmpty(filePath)) return null!;

            // The decode width is part of the key: a 96px tile and a 200px tile are
            // different bitmaps, and handing one where the other is wanted is a blurry
            // preview, not a speed-up.
            int decode = 200;
            if (parameter is string spec && int.TryParse(spec, out int requested) && requested > 0)
                decode = requested;

            string key = decode + "|" + filePath;
            if (Cache.TryGetValue(key, out var cached)) return cached!;

            var bitmap = Load(filePath, decode);

            if (Cache.Count >= MaxCached) Cache.Clear();
            Cache[key] = bitmap;

            return bitmap!;
        }

        private static BitmapSource? Load(string filePath, int decode)
        {
            string thumbPath = Path.Combine(
                Path.GetTempPath(), "PhotoBookRenamer", "Thumbnails",
                GetFilePathHash(filePath) + "_thumb.jpg");

            if (File.Exists(thumbPath))
            {
                try
                {
                    // OnLoad: the file is read into memory and released at once, so a
                    // hundred open thumbnails cannot hold a hundred file handles.
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(thumbPath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
                catch
                {
                    // Fall through to the original rather than showing nothing.
                }
            }

            if (!File.Exists(filePath)) return null;

            try
            {
                // DecodePixelWidth bounds the memory: decoding a 18 MB print file at full
                // size for a 48px tile is the single most expensive thing this program
                // could do, and it is what the thumbnail cache exists to avoid.
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = decode;
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// A stable per-path id, computed once. Sixteen hex characters of SHA-256 is more
        /// than enough to keep two photos with the same file name apart, and it is what
        /// the thumbnail files on disk are named after - so the name cannot change.
        /// </summary>
        private static string GetFilePathHash(string filePath)
        {
            if (Hashes.TryGetValue(filePath, out var known)) return known;

            string hash;
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(filePath);
                hash = BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", "").Substring(0, 16);
            }

            Hashes[filePath] = hash;
            return hash;
        }

        /// <summary>
        /// Drops the decoded bitmaps. Called where the program already drops the slot
        /// thumbnails, so a rebuilt preview is not served from the cache.
        /// </summary>
        public static void ClearCache()
        {
            Cache.Clear();
            Hashes.Clear();
        }

        public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
