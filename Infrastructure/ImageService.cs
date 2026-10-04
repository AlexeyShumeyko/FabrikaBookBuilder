using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SixLabors.ImageSharp;

namespace PhotoBookRenamer.Infrastructure
{
    /// <summary>
    /// Reads what a photograph says about itself: how large it is, and which one in a folder
    /// is the cover.
    ///
    /// <para>
    /// Only the header is read - ImageSharp's <c>Identify</c> - so this stays cheap enough to
    /// run over every photograph in a project while it opens. Reduced copies are somebody
    /// else's job: see <see cref="ThumbnailProvider"/>.
    /// </para>
    /// </summary>
    public class ImageService : IImageService
    {
        private readonly Dictionary<string, (int Width, int Height)> _dimensionCache = new();
        private readonly object _cacheLock = new();

        public async Task<(int Width, int Height)> GetImageDimensionsAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return (0, 0);

            lock (_cacheLock)
            {
                if (_dimensionCache.TryGetValue(filePath, out var cached))
                    return cached;
            }

            return await Task.Run(() =>
            {
                try
                {
                    var imageInfo = Image.Identify(filePath);
                    if (imageInfo != null)
                    {
                        var dimensions = (imageInfo.Width, imageInfo.Height);
                        lock (_cacheLock) { _dimensionCache[filePath] = dimensions; }
                        return dimensions;
                    }
                    return (0, 0);
                }
                catch
                {
                    return (0, 0);
                }
            });
        }

        /// <summary>
        /// The photograph with the most pixels, which is the one a typesetter printed largest
        /// and therefore the cover of its folder. A photograph that cannot be read counts as
        /// zero and is skipped.
        /// </summary>
        public async Task<string?> DetectCoverAsync(string[] filePaths)
        {
            if (filePaths == null || filePaths.Length == 0)
                return null;

            var tasks = filePaths.Select(async filePath =>
            {
                var (width, height) = await GetImageDimensionsAsync(filePath);
                return new { FilePath = filePath, Pixels = (long)width * height };
            });

            var results = await Task.WhenAll(tasks);
            return results.OrderByDescending(r => r.Pixels).FirstOrDefault()?.FilePath;
        }
    }
}
