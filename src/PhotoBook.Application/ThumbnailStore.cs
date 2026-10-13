using System;
using System.Collections.Concurrent;
using System.IO;
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
        private static readonly Lazy<string> DirectoryPath = new(() =>
            Path.Combine(Path.GetTempPath(), "PhotoBookRenamer", "Thumbnails"));

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
            Path.Combine(Directory, $"{GetHash(sourcePath)}_thumb.jpg");

        /// <summary>The reduced copy if it already exists, otherwise null. Does not create one.</summary>
        public static string? GetExistingPath(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath)) return null;
            var path = GetPath(sourcePath);
            return File.Exists(path) ? path : null;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Hashes =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
