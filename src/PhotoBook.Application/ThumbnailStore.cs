using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace PhotoBook.Application
{
    /// <summary>
    /// Where a reduced copy of a photograph lives.
    ///
    /// <para>
    /// This used to be written out four times - in the image service, in two converters and
    /// in a view model - and they agreed only because nobody had changed one of them. A
    /// converter has no constructor to inject into, so it cannot ask anyone where the file
    /// is; it has to know. The formula therefore lives here, once, and everything that shows
    /// or builds a preview asks this.
    /// </para>
    ///
    /// <para>
    /// The name is derived from the full source path, so two photographs with the same file
    /// name in different folders never collide. Sixteen hexadecimal characters of SHA-256 is
    /// more than enough for that and short enough to read in a directory listing.
    /// </para>
    ///
    /// <para>
    /// The directory is under the user's temporary folder. It is a cache: deleting it costs
    /// time, never correctness. Moving it next to the logs is a separate decision, because it
    /// invalidates every preview that exists today.
    /// </para>
    /// </summary>
    public static class ThumbnailStore
    {
        /// <summary>
        /// Largest side of a reduced copy, and part of its file name.
        /// </summary>
        /// <remarks>
        /// A slot on the combined screen draws at about 190 layout units, but a 4K screen at
        /// 150-200 percent makes that 300-400 device pixels - and 320 was upscaled there,
        /// which is the softness the photographer sees when trying to tell two frames apart.
        /// 512 covers the largest cell without decoding the original. It was 500 before the
        /// Avalonia move and 320 after it; only the copy written for every photograph gets
        /// bigger, the number of reads stays the same.
        ///
        /// The number is in the name because changing it has to invalidate the cache: a
        /// 320-pixel copy left behind under the old name would be read as the new one.
        /// </remarks>
        public const int MaxPixelSize = 512;

        private static readonly Lazy<string> DirectoryPath = new(() =>
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PhotoBookRenamer", "Thumbnails"));

        /// <summary>Folder holding the reduced copies. Created when something writes into it.</summary>
        public static string Directory => DirectoryPath.Value;

        /// <summary>A stable per-source-path id, computed once per path per process.</summary>
        public static string GetHash(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath))
                throw new ArgumentException("A source path is required.", nameof(sourcePath));

            return Hashes.GetOrAdd(sourcePath, path =>
            {
                using var sha = SHA256.Create();
                var bytes = Encoding.UTF8.GetBytes(path);
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").Substring(0, 16);
            });
        }

        /// <summary>The file a reduced copy of this photograph would be written to.</summary>
        public static string GetPath(string sourcePath) =>
            System.IO.Path.Combine(Directory, $"{GetHash(sourcePath)}_thumb{MaxPixelSize}.jpg");

        /// <summary>
        /// The path a reduced copy would have, whether or not one is there. Callers that
        /// need to know whether to build one ask the file system themselves: this class only
        /// knows how to spell the path. It used to answer with File.Exists, which put a file
        /// system call inside the layer that decides what a project is - the last one of its
        /// kind after the index store was extracted.
        /// </summary>
        public static string? PathOrNull(string sourcePath)
            => string.IsNullOrEmpty(sourcePath) ? null : GetPath(sourcePath);

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Hashes =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
