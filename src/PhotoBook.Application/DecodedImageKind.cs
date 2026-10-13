namespace PhotoBook.Application
{
    /// <summary>Which of the two decoded-image caches an entry belongs to.</summary>
    /// <remarks>
    /// They are separate because they cost different things and are stale for different
    /// reasons: a page image is a full-size photo decoded from the original, and a thumbnail
    /// is a small copy. Dropping one does not make the other wrong.
    ///
    /// The distinction has to stay visible to callers. Several places invalidate page images
    /// on their own, and a single combined "clear everything" would silently throw away
    /// thumbnails too - which costs a regenerate from disk on the next paint, on a screen
    /// that did not need it. A cache is safe to clear more than necessary; it is not free.
    /// </remarks>
    public enum DecodedImageKind
    {
        /// <summary>
        /// The full-size photo behind a spread or slot. Decoded from the original file.
        /// </summary>
        Page,

        /// <summary>
        /// The small preview behind a pool tile, a project card strip or a list row.
        /// </summary>
        Thumbnail
    }
}
