using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBookRenamer.Presentation.Converters;

namespace PhotoBookRenamer.Presentation.Services
{
    /// <summary>
    /// The WPF answer to <see cref="IDecodedImageCache"/>: the two bitmap caches the
    /// converters keep, addressed as one contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class holds no cache of its own. The caches live in the converters, because the
    /// converters are what fill them - a cache kept anywhere else would need to be told
    /// about every image it was supposed to have, and would then be wrong by exactly as
    /// much as it was told.
    /// </para>
    /// <para>
    /// The caches are static today, which is why this is a stateless adapter rather than a
    /// cache. That is a property of the WPF side, not of the contract: an Avalonia
    /// implementation is free to hold its cache as instance state, because nothing in
    /// <see cref="IDecodedImageCache"/> asks for an image back or names a type.
    /// </para>
    /// </remarks>
    internal class WpfDecodedImageCache : IDecodedImageCache
    {
        public void Invalidate(DecodedImageKind kind)
        {
            switch (kind)
            {
                case DecodedImageKind.Page:
                    PageSourceConverter.ClearCache();
                    break;

                case DecodedImageKind.Thumbnail:
                    FilePathToThumbnailConverter.ClearCache();
                    break;
            }
        }

        public void InvalidateAll()
        {
            // Two calls rather than one loop: each cache already has the call it needs, and
            // routing both through Invalidate would re-decide the same switch twice.
            Invalidate(DecodedImageKind.Page);
            Invalidate(DecodedImageKind.Thumbnail);
        }

        public void InvalidateFile(string filePath)
        {
            PageSourceConverter.ClearCacheForFile(filePath);
            FilePathToThumbnailConverter.ClearCacheForFile(filePath);
        }

        public Task PrewarmAsync(IEnumerable<string>? filePaths, CancellationToken cancellationToken = default)
            => PageSourceConverter.PrewarmAsync(filePaths, cancellationToken);
    }
}
