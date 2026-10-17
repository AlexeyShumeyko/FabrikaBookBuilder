using System.IO;
using PhotoBook.Application;

namespace PhotoBook.Shell.Converters
{
    /// <summary>
    /// Which file to draw when a small picture is wanted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A card on the projects screen draws three photographs about forty pixels across. It
    /// was drawing the originals to do it - files of around three megabytes each, decoded on
    /// the interface thread to paint a list of names, and that was measured at roughly
    /// 350 ms of the time it takes to come back to this screen.
    /// </para>
    /// <para>
    /// The thumbnails already exist. They are not stored inside the project - they are a
    /// cache under the temporary folder, and their path is <b>derived</b> from the original's
    /// (a hash of it, plus a suffix), so there is nothing to record anywhere and nothing to
    /// keep in step: the same original always names the same thumbnail.
    /// </para>
    /// <para>
    /// That also answers when they are missing. They are made when a project is worked on,
    /// so a project somebody has actually built has them - and they survive because the
    /// cache is disposable, not because the project depends on it. Clear the cache and they
    /// are made again; until then every list in the program draws them.
    /// </para>
    /// <para>
    /// Falling back to the original is not a second-class path: it is what happens to a
    /// project nobody has opened since the cache was cleared, and it is correct, just
    /// slower. It is also why this is a resolver and not a rule - a caller that genuinely
    /// wants the whole picture can say so.
    /// </para>
    /// </remarks>
    internal static class PreviewFile
    {
        /// <summary>
        /// The thumbnail for this original if there is one, otherwise the original itself.
        /// </summary>
        public static string? PreferThumbnail(string? sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath)) return null;

            try
            {
                var thumbnail = ThumbnailStore.GetPath(sourcePath);
                return File.Exists(thumbnail) ? thumbnail : sourcePath;
            }
            catch
            {
                // A path that cannot even be hashed is a path that cannot be drawn either;
                // the caller's loader will say so in its own words.
                return sourcePath;
            }
        }
    }
}
