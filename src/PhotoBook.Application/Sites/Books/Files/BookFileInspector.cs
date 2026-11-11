using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites.Catalog.Layout;

namespace PhotoBook.Application.Sites.Books.Files
{
    /// <summary>Reads each page's presence and pixel size through the program's own ports.</summary>
    /// <remarks>
    /// <para>
    /// A missing file is not asked for its size: the image reader would have to look, find
    /// nothing and say zero, which is a disk access for an answer already known. A file that is
    /// there but answers zero is a different thing - damaged, or not a picture at all - and is
    /// reported as unreadable rather than as missing.
    /// </para>
    /// <para>
    /// Pages are read one at a time. The reader keeps its own header cache, so a second look at
    /// the same photograph costs nothing, and reading four pages at once to save a second would
    /// spend the customer's disk on a screen that is not showing anything yet.
    /// </para>
    /// </remarks>
    public sealed class BookFileInspector : IBookFileInspector
    {
        private readonly IFileService _files;
        private readonly IImageService _images;

        public BookFileInspector(IFileService files, IImageService images)
        {
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _images = images ?? throw new ArgumentNullException(nameof(images));
        }

        public async Task<IReadOnlyList<PageFileFacts>> InspectAsync(
            IReadOnlyList<PageFile> files,
            CancellationToken cancellationToken = default)
        {
            if (files is null)
                throw new ArgumentNullException(nameof(files));

            var facts = new List<PageFileFacts>(files.Count);

            for (var index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var path = files[index]?.Path ?? string.Empty;
                var exists = path.Length > 0 && _files.FileExists(path);

                facts.Add(new PageFileFacts(
                    path,
                    exists,
                    exists ? await SizeOfAsync(path).ConfigureAwait(false) : null));
            }

            return facts;
        }

        /// <summary>The picture's size, or null when the file is there but says nothing.</summary>
        private async Task<PixelSize?> SizeOfAsync(string path)
        {
            var (width, height) = await _images
                .GetImageDimensionsAsync(path)
                .ConfigureAwait(false);

            return width > 0 && height > 0 ? new PixelSize(width, height) : null;
        }
    }
}
