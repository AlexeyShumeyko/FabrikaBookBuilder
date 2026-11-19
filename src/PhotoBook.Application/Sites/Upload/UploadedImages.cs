using System;
using System.Collections.Generic;

namespace PhotoBook.Application.Sites.Upload
{
    /// <summary>Which picture the site already holds for a source file, so it is not sent twice.</summary>
    /// <remarks>
    /// <para>
    /// A combined order puts the same photograph into every book: thirty books of nine spreads
    /// hold the same ten files. Sending all 270 sends the same bytes 27 times. The site's own
    /// uploader recognises a picture it already holds and asks for a copy instead - a request
    /// naming the stored picture and no file at all - which is what turns 270 uploads into 10.
    /// </para>
    /// <para>
    /// The record is per project, because the number the site gives a picture belongs to one
    /// project and means nothing in another. It lives for the run and is thrown away with it:
    /// a project the customer sends again is a new project on the site.
    /// </para>
    /// </remarks>
    public sealed class UploadedImages
    {
        private readonly Dictionary<string, long> _imageIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new();

        /// <summary>The picture stored for this file, when the site has already named one.</summary>
        public bool TryGet(string? path, out long imageId)
        {
            lock (_gate)
                return _imageIds.TryGetValue(path ?? string.Empty, out imageId);
        }

        /// <summary>Keeps the number the site gave the picture. Zero says it named none.</summary>
        public void Remember(string? path, long imageId)
        {
            if (string.IsNullOrWhiteSpace(path) || imageId <= 0)
                return;

            lock (_gate)
                _imageIds[path] = imageId;
        }

        /// <summary>How many distinct pictures the site holds for the run.</summary>
        public int Count
        {
            get
            {
                lock (_gate)
                    return _imageIds.Count;
            }
        }
    }
}
