using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using PhotoBook.Application;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace PhotoBookRenamer.Infrastructure
{
    /// <summary>
    /// Builds reduced copies with ImageSharp, which decodes the whole photograph and then
    /// shrinks it.
    ///
    /// <para>
    /// Decoding is unavoidable here: it is what ImageSharp does. What is avoidable is doing
    /// it for every photograph at once. The previous version started one task per file and
    /// let the thread pool take them.
    /// </para>
    ///
    /// <para>
    /// How many are done at once is a trade-off between time and memory, and each decoded
    /// original occupies about 96 MB. Measured on 180 photographs of 650 MB on a
    /// twelve-core machine:
    /// </para>
    ///
    /// <code>
    /// threads   time    peak private
    ///      1   35.9 s        196 MB
    ///      2   18.4 s        355 MB
    ///      4   10.0 s        632 MB
    ///      8    7.1 s      1247 MB
    ///     16    6.1 s      1984 MB
    /// </code>
    ///
    /// <para>
    /// Four is the default: it halves the time of a single thread and stays under a
    /// gigabyte on a machine that is also running a browser and an office suite. A machine
    /// with eight or more threads is faster and fits, on paper, into eight gigabytes of
    /// memory - and into none of them once the machine is not idle.
    /// </para>
    /// </summary>
    public class ThumbnailProvider : IThumbnailProvider
    {
        /// <summary>Larger side of a reduced copy. A card cell is about 190 px tall.</summary>
        public const int MaxSize = 500;

        private readonly SemaphoreSlim _gate;
        private readonly int _maxConcurrency;

        public ThumbnailProvider(int maxConcurrency = 4)
        {
            _maxConcurrency = Math.Clamp(maxConcurrency, 1, 32);
            _gate = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
        }

        public string? GetExistingPath(string sourcePath) => ThumbnailStore.GetExistingPath(sourcePath);

        public async Task<string?> GetOrCreateAsync(string sourcePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                return null;

            var existing = ThumbnailStore.GetExistingPath(sourcePath);
            if (existing != null && IsReadable(existing))
                return existing;

            var created = await CreateAsync(sourcePath, ThumbnailStore.GetPath(sourcePath), cancellationToken)
                               .ConfigureAwait(false);

            // Inside a batch a photograph that cannot be read is skipped, and an empty
            // string is how that is reported. Here the caller asked a question it can act
            // on, and no preview is the answer.
            return string.IsNullOrEmpty(created) ? null : created;
        }

        public async Task EnsureAsync(
            IReadOnlyCollection<string> sourcePaths,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (sourcePaths.Count == 0) return;

            int done = 0;
            var queue = new ConcurrentQueue<string>(sourcePaths);

            async Task WorkerAsync()
            {
                while (queue.TryDequeue(out var path))
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        var existing = ThumbnailStore.GetExistingPath(path);
                        if (existing == null || !IsReadable(existing))
                        {
                            await CreateAsync(path, ThumbnailStore.GetPath(path), cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }

                    int completed = Interlocked.Increment(ref done);
                    progress?.Report((double)completed / sourcePaths.Count);
                }
            }

            var workers = Enumerable.Range(0, _maxConcurrency).Select(_ => WorkerAsync()).ToArray();
            await Task.WhenAll(workers).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes one reduced copy. A photograph that cannot be read yields an empty string:
        /// a damaged file is reported by the cell that shows it and written to the log, and it
        /// must not fail the rest of the project.
        /// </summary>
        private async Task<string> CreateAsync(string sourcePath, string thumbnailPath, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        var directory = Path.GetDirectoryName(thumbnailPath);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                            Directory.CreateDirectory(directory);

                        using var image = Image.Load(sourcePath);
                        var ratio = Math.Min((double)MaxSize / image.Width, (double)MaxSize / image.Height);
                        var target = new Size((int)(image.Width * ratio), (int)(image.Height * ratio));

                        if (image.Width <= MaxSize && image.Height <= MaxSize)
                        {
                            Write(image, thumbnailPath);
                            return thumbnailPath;
                        }

                        image.Mutate(x => x.Resize(new ResizeOptions
                        {
                            Size = target,
                            Mode = ResizeMode.Max,
                            Sampler = KnownResamplers.Lanczos3
                        }));
                        Write(image, thumbnailPath);
                        return thumbnailPath;
                    }
                    catch
                    {
                        return string.Empty;
                    }
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private static void Write(Image image, string path)
        {
            var encoder = new JpegEncoder { Quality = 85 };
            if (File.Exists(path))
            {
                try { File.Delete(path); }
                catch { /* a locked file is replaced by the write below, or reported there */ }
            }
            image.SaveAsJpeg(path, encoder);
        }

        /// <summary>
        /// A reduced copy that exists but cannot be opened is worth less than none: it is
        /// deleted and written again rather than shown as an empty cell.
        /// </summary>
        private static bool IsReadable(string thumbnailPath)
        {
            try
            {
                using var stream = File.Open(thumbnailPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return true;
            }
            catch
            {
                try { File.Delete(thumbnailPath); } catch { }
                return false;
            }
        }
    }
}
