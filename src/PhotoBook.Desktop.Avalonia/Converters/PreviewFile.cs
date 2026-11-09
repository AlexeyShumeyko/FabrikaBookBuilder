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
    /// There is deliberately <b>no</b> fallback to the original. There used to be, and it was
    /// measured at 1.1 s of the 1.9 s the projects screen took to appear: eighteen cards were
    /// decoding a five-to-twenty-four-megapixel print file to fill a forty-eight-pixel tile,
    /// because no reduced copy had been built for them yet. A tile with nothing in it is
    /// better than a tile that costs the screen a second, and the missing copies are built
    /// in the background by the screen that asks for them.
    /// </para>
    /// <para>
    /// A caller that genuinely wants the whole picture asks for it by name - the converter
    /// takes <c>original</c> as its parameter.
    /// </para>
    /// </remarks>
    internal static class PreviewFile
    {
        /// <summary>
        /// The reduced copy for this original, or null when there is none yet.
        /// </summary>
        public static string? PreferThumbnail(string? sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath)) return null;

            try
            {
                var thumbnail = ThumbnailStore.GetPath(sourcePath);
                return File.Exists(thumbnail) ? thumbnail : null;
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
