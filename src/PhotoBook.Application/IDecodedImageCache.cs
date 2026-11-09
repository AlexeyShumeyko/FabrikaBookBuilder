using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoBook.Application
{
    /// <summary>
    /// Tells the shell when the pictures it is holding are no longer true, and asks it to
    /// have the next ones ready before they are needed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A decoded image is the most expensive thing in the program to produce and the most
    /// expensive to get wrong: a 19 MB photo decoded on the UI thread freezes the window,
    /// and one kept too long after the file changed shows a picture that is no longer on
    /// disk. So the shell keeps them and the view models say when to let go.
    /// </para>
    /// <para>
    /// Nothing here returns an image. That is the point: an image object is a type of one
    /// engine, and a contract that handed one back would bind every caller to it. Only the
    /// shell knows how to hold a decoded picture, and only the shell benefits from holding
    /// this cache - the callers want the two facts it already has, namely that a file has
    /// changed and that some files are about to be shown.
    /// </para>
    /// <para>
    /// A cache is always safe to drop too much of: a miss costs a decode, never a wrong
    /// picture. That is why these methods say what to forget and never what to remember.
    /// </para>
    /// </remarks>
    public interface IDecodedImageCache
    {
        /// <summary>
        /// Drops everything held for one kind, leaving the other kind alone.
        /// </summary>
        void Invalidate(DecodedImageKind kind);

        /// <summary>
        /// Drops everything of both kinds.
        /// </summary>
        void InvalidateAll();

        /// <summary>
        /// Drops the entry for one file, for both kinds.
        /// </summary>
        /// <remarks>
        /// Used when a single photograph is replaced, renamed or removed - the case where
        /// clearing the whole cache would be correct but would cost a full re-decode of
        /// every image on screen for the sake of one file.
        /// </remarks>
        void InvalidateFile(string filePath);

        /// <summary>
        /// Decodes ahead of the first paint so that the first render is a cache hit.
        /// </summary>
        /// <remarks>
        /// Called when a project opens or a large batch of photographs is loaded. Decoding
        /// on demand means the first render pays for every image at once, on the thread that
        /// draws; decoding here means the cost is paid in the background and the window
        /// answers a click while it happens.
        ///
        /// Paths that are missing, duplicated or empty are skipped, and a decode that fails
        /// is not an error - a photograph that cannot be read is reported by the slot that
        /// shows it, not by this call.
        /// </remarks>
        Task PrewarmAsync(IEnumerable<string>? filePaths, CancellationToken cancellationToken = default);
    }
}
