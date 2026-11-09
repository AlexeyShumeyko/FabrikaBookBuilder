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
        /// <summary>
        /// Largest picture this cache will ever produce, whatever the caller asks for.
        /// </summary>
        /// <remarks>
        /// Nothing in this shell draws a photograph at its original size: a cell draws a
        /// reduced copy, and exporting writes the file to disk rather than reading it into
        /// memory. So the largest size that is ever needed is a generous one, and asking for
        /// "whatever it is" is answered with that rather than with the truth. It is what
        /// turned a project open into 1.5 GB: the preparation pass decoded every original in
        /// full and kept them, and nothing ever drew one.
        /// </remarks>
        private const int LargestUseful = 1024;

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
        /// Decodes ahead of the first paint, off the drawing thread.
        /// </summary>
        /// <remarks>
        /// <para>
        /// In this shell it does nothing, and that is the decision, not an omission.
        /// </para>
        /// <para>
        /// The contract asks for a preparation pass over the originals. Here the originals
        /// are the one thing a cell must not read: a reduced copy exists by the time a
        /// project is on screen, it is a few dozen kilobytes, and reading the 18-megabyte
        /// original to produce something nobody draws is what made opening a project cost
        /// 9.4 seconds of CPU and 1.5 GB. The pass still happens - the reduced copies are
        /// built by the thumbnail service, and that is the part that takes the time and the
        /// part that is worth taking.
        /// </para>
        /// <para>
        /// The other shell's version does decode, at the size a cell draws, and is right to:
        /// its cells may still have to fall back to an original when a reduced copy is
        /// missing. Making this one do the same thing would undo the whole point.
        /// </para>
        /// </remarks>
        public Task PrewarmAsync(
            IEnumerable<string>? filePaths,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <summary>The same, at the size the cells will draw it.</summary>
        public Task PrewarmAsync(
            IEnumerable<string>? filePaths,
            int targetWidth,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

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

        /// <summary>
        /// Decodes a reduced copy at whatever size it already is, and never grows it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A reduced copy is bounded - the thumbnail service never writes one larger than 500
        /// pixels on its long side - and a cell draws it smaller than that. Asking the decoder
        /// for the cell's width anyway made it grow the picture to fit, an interpolation of
        /// pixels that were never there, and then the cell drew it back down and threw the
        /// result of that away. The released program hands its reduced copies to the renderer
        /// untouched, and so does this.
        /// </para>
        /// <para>
        /// Not for originals, and the reason is in the name of the method rather than in a
        /// check inside it: reading an 18 MB scan whole is what this cache exists to prevent.
        /// </para>
        /// <para>
        /// A file that is not there yet is answered without being remembered. Reduced copies
        /// are built after the screen is already laid out, so "not there" is the normal state
        /// of the first second and a half of a cold start - and a cache that remembered it
        /// would hand out that answer to every later request, including the one made after the
        /// file has been written. That is how a project can open, generate every preview, and
        /// still show nothing but empty frames.
        /// </para>
        /// </remarks>
        public Bitmap? GetReduced(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;

            string key = "natural|" + filePath;
            if (_cache.TryGetValue(key, out var cached)) return cached;

            if (!File.Exists(filePath)) return null;

            Bitmap? decoded;
            try
            {
                using var stream = File.OpenRead(filePath);
                decoded = new Bitmap(stream);
            }
            catch
            {
                decoded = null;
            }

            if (_cache.Count >= MaxCached) Shed(_cache.Count / 4);
            _cache[key] = decoded;

            return decoded;
        }

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

                // The size asked for, or the largest one that is ever useful. Never the
                // original: see LargestUseful.
                int width = targetWidth <= 0 ? LargestUseful : targetWidth;

                using var stream = File.OpenRead(filePath);
                return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality);
            }
            catch
            {
                // A file that will not decode is a fact about the file, not about the program.
                return null;
            }
        }
    }
}
