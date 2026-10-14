using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using PhotoBook.Application;

namespace PhotoBook.Shell.Services
{
    /// <summary>
    /// Decoded images, held once and shared.
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
    /// </remarks>
    public class AvaloniaImageCache : IDecodedImageCache
    {
        /// <summary>
        /// Past this many images the whole cache is dropped rather than grown. A cache that
        /// grows without limit on a weak machine is just a slower leak.
        /// </summary>
        private const int MaxCached = 400;

        private readonly ConcurrentDictionary<string, Bitmap?> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        public void Invalidate(DecodedImageKind kind) => _cache.Clear();

        public void InvalidateAll() => _cache.Clear();

        public void InvalidateFile(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath))
                _cache.TryRemove(filePath, out _);
        }

        /// <summary>
        /// Decodes ahead of the first paint, off the drawing thread. A photograph that cannot
        /// be read is not an error here: the tile that shows it says so.
        /// </summary>
        public async Task PrewarmAsync(
            IEnumerable<string>? filePaths,
            CancellationToken cancellationToken = default)
        {
            if (filePaths is null) return;

            foreach (var path in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(path) || _cache.ContainsKey(path)) continue;

                await Task.Run(() => Decode(path), cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The decoded image for a path, or null. Null covers both "not decoded yet" and
        /// "cannot be read", because the tile looks the same either way and the difference is
        /// reported by the slot, not by this method.
        /// </summary>
        public Bitmap? Get(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;
            if (_cache.TryGetValue(filePath, out var cached)) return cached;

            var decoded = Decode(filePath);
            if (_cache.Count >= MaxCached) _cache.Clear();
            _cache[filePath] = decoded;

            return decoded;
        }

        private static Bitmap? Decode(string filePath)
        {
            try
            {
                if (!System.IO.File.Exists(filePath)) return null;
                return new Bitmap(filePath);
            }
            catch
            {
                // A file that will not decode is a fact about the file, not about the program.
                return null;
            }
        }
    }
}
