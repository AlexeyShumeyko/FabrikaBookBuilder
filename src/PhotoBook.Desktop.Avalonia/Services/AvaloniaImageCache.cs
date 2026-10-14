using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using PhotoBook.Application;

namespace PhotoBook.Shell.Services
{
    /// <summary>
    /// Decoded images, held once and shared, and decoded only as large as they are drawn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same shape as the WPF side and for the same reason: a photograph shown in two
    /// places must be decoded once, and a row whose binding re-evaluates must not decode it
    /// again. That was measured, not assumed - decoding per call was the single largest cost
    /// in the file list.
    /// </para>
    /// <para>
    /// Unlike the WPF caches this one is an instance rather than a static, because Avalonia's
    /// bitmaps are owned by the application and there is a natural place to hang one. The
    /// contract does not care either way: nothing in <see cref="IDecodedImageCache"/> asks for
    /// an image back or names a type, which is exactly why the port survived the move.
    /// </para>
    /// <para>
    /// <b>The size is part of the key.</b> Decoding at full size and letting the compositor
    /// scale it down is what this did first, and it is why the file list froze and why the
    /// scrolling stuttered: a 48-pixel tile was being decoded from a four-thousand-pixel
    /// photograph, on the thread that draws. The other shell asks for 96 pixels and gets 96
    /// pixels. This asks for whatever the cell is going to draw.
    /// </para>
    /// </remarks>
    public class AvaloniaImageCache : IDecodedImageCache
    {
        /// <summary>
        /// Past this many images the cache sheds a quarter of itself rather than growing or
        /// emptying outright.
        /// </summary>
        /// <remarks>
        /// Emptying outright is worse than growing, and not by a little: scroll far enough to
        /// cross the limit and every image on screen is gone, so every image on screen is
        /// decoded again - which is a stutter that grows with the folder. Shedding a quarter
        /// means the worst case is a quarter of them, and it cannot compound.
        /// </remarks>
        private const int MaxCached = 600;

        private readonly ConcurrentDictionary<string, Bitmap?> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        public void Invalidate(DecodedImageKind kind) => Shed(_cache.Count);

        public void InvalidateAll() => _cache.Clear();

        public void InvalidateFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            foreach (var key in _cache.Keys)
            {
                if (key.EndsWith(filePath, StringComparison.OrdinalIgnoreCase))
                    _cache.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// Decodes ahead of the first paint, off the drawing thread. A photograph that cannot
        /// be read is not an error here: the tile that shows it says so.
        /// </summary>
        public Task PrewarmAsync(
            IEnumerable<string>? filePaths,
            CancellationToken cancellationToken = default)
            => PrewarmAsync(filePaths, 0, cancellationToken);

        /// <summary>The same, at the size the cells will draw it.</summary>
        public async Task PrewarmAsync(
            IEnumerable<string>? filePaths,
            int targetWidth,
            CancellationToken cancellationToken = default)
        {
            if (filePaths is null) return;

            foreach (var path in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(path) || _cache.ContainsKey(Key(path, targetWidth))) continue;

                await Task.Run(() => Decode(path, targetWidth), cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The decoded image for a path at a given width, or null. Null covers both "not
        /// decoded yet" and "cannot be read", because the tile looks the same either way and
        /// the difference is reported by the slot, not by this method.
        /// </summary>
        public Bitmap? Get(string filePath, int targetWidth)
        {
            if (string.IsNullOrEmpty(filePath)) return null;

            string key = Key(filePath, targetWidth);
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var decoded = Decode(filePath, targetWidth);

            if (_cache.Count >= MaxCached) Shed(_cache.Count / 4);
            _cache[key] = decoded;

            return decoded;
        }

        /// <summary>Decodes at full size. Only for the one place that really does need it.</summary>
        public Bitmap? Get(string filePath) => Get(filePath, 0);

        private static string Key(string filePath, int targetWidth) => targetWidth + "|" + filePath;

        private void Shed(int count)
        {
            foreach (var key in _cache.Keys.Take(Math.Max(1, count)))
                _cache.TryRemove(key, out _);
        }

        private static Bitmap? Decode(string filePath, int targetWidth)
        {
            try
            {
                if (!File.Exists(filePath)) return null;

                // Zero means "whatever it is", and the sized call is the whole point of this
                // class: decoding a 48-pixel tile out of a four-thousand-pixel photograph is
                // the single most expensive thing a file list can do on the thread that draws.
                if (targetWidth <= 0) return new Bitmap(filePath);

                using var stream = File.OpenRead(filePath);
                return Bitmap.DecodeToWidth(stream, targetWidth, BitmapInterpolationMode.HighQuality);
            }
            catch
            {
                // A file that will not decode is a fact about the file, not about the program.
                return null;
            }
        }
    }
}
