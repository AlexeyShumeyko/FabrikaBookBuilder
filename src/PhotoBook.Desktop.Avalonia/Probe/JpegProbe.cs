using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace PhotoBook.Shell
{
    /// <summary>
    /// Step 3.0: the question that can stop the migration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole design rests on never touching the originals. Opening a project must show
    /// a hundred 2-3 MB photographs as 48-pixel tiles, and it must feel instant. The WPF
    /// shell gets that from <c>BitmapImage.DecodePixelWidth</c>, which hands the target size
    /// to the JPEG decoder so the pixels are never fully materialised.
    /// </para>
    /// <para>
    /// So this measures two ways of getting the same tile, because they are not the same
    /// work and it would be easy to measure only the fast one and declare victory:
    /// </para>
    /// <list type="number">
    /// <item>decode straight to the tile size, if the platform offers it;</item>
    /// <item>decode the whole photograph and scale it down afterwards.</item>
    /// </list>
    /// <para>
    /// If (1) is not available, or if it is not clearly faster than (2), the design has to
    /// change before a single screen is written - either a decode helper process, or
    /// thumbnails produced ahead of time. Both are worth knowing now and neither is worth
    /// discovering at the end.
    /// </para>
    /// </remarks>
    internal static class JpegProbe
    {
        /// <summary>
        /// Decodes straight to a tile, the way the WPF shell does with DecodePixelWidth.
        /// Avalonia takes a stream rather than a path here, so the file is opened, decoded
        /// and disposed every time - which is what the WPF path does too, and is why the
        /// measurement is fair.
        /// </summary>
        private static Bitmap Decode(string path)
        {
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, TileSize, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
        }

        /// <summary>Tile size the product actually uses for the pool and the slot frames.</summary>
        public const int TileSize = 48;

        public static string Run(string folder, int photos, int repeats)
        {
            // Recursive: the photographs sit in per-roll subfolders, and a measurement over
            // a folder that happens to be empty is not a measurement.
            var files = Directory.GetFiles(folder, "*.jpg", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(folder, "*.jpeg", SearchOption.AllDirectories))
                .OrderBy(f => f)
                .Take(photos)
                .ToArray();

            if (files.Length == 0) return "no jpeg files in " + folder;

            var report = new System.Text.StringBuilder();
            report.AppendLine($"photos: {files.Length}, tile: {TileSize}px, repeats: {repeats}");

            var biggest = files.OrderByDescending(f => new FileInfo(f).Length).First();
            report.AppendLine($"largest: {Path.GetFileName(biggest)} ({new FileInfo(biggest).Length / 1024} KB)");

            // --- 1. straight to the tile size -------------------------------------------
            bool directAvailable = true;
            double directMs = 0;
            try
            {
                // Warm the decoder so the first file's JIT is not counted as decoding.
                using (Decode(biggest)) { }

                var sw = Stopwatch.StartNew();
                for (var r = 0; r < repeats; r++)
                {
                    foreach (var f in files)
                    {
                        using var bmp = Decode(f);
                    }
                }
                directMs = sw.Elapsed.TotalMilliseconds / (repeats * files.Length);
            }
            catch (Exception ex)
            {
                directAvailable = false;
                report.AppendLine($"decode-to-size: NOT AVAILABLE ({ex.GetType().Name})");
            }

            if (directAvailable)
            {
                report.AppendLine($"decode-to-size:      {directMs:F2} ms per photograph");
            }

            // --- 2. full decode, then scale ----------------------------------------------
            // Warm it the same way.
            using (new Bitmap(biggest)) { }

            var sw2 = Stopwatch.StartNew();
            for (var r = 0; r < repeats; r++)
            {
                foreach (var f in files)
                {
                    using var full = new Bitmap(f);
                    using var small = full.CreateScaledBitmap(new PixelSize(TileSize, TileSize));
                }
            }
            var scaledMs = sw2.Elapsed.TotalMilliseconds / (repeats * files.Length);
            report.AppendLine($"full then scale:     {scaledMs:F2} ms per photograph");

            if (directAvailable)
            {
                var ratio = scaledMs / Math.Max(directMs, 0.0001);
                report.AppendLine($"ratio:               {ratio:F1}x");
                report.AppendLine(ratio >= 1.5
                    ? "verdict:            decoding straight to size is worth keeping"
                    : "verdict:            no measurable gain - a decode helper may not be needed");
            }

            // --- 3. memory ----------------------------------------------------------------
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = GC.GetTotalMemory(true);

            var held = files.Select(f => Decode(f)).ToArray();
            var after = GC.GetTotalMemory(false);
            report.AppendLine($"memory for {held.Length} tiles at {TileSize}px: {((after - before) / 1024.0):F0} KB");
            foreach (var b in held) b.Dispose();

            report.AppendLine($"managed heap now:   {GC.GetTotalMemory(false) / 1024 / 1024} MB");
            report.AppendLine($"process working set:{Environment.WorkingSet / 1024 / 1024} MB");

            return report.ToString();
        }
    }
}