using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using PhotoBook.Application;

namespace PhotoBook.Shell.Services;

/// <summary>
/// The first photographs of a folder, reduced to the size a cover draws them at.
/// </summary>
/// <remarks>
/// <para>
/// The folder panel used to draw one gold folder shape per folder, which says nothing about
/// what is inside. Four small photographs say it at a glance, and that is worth the reads -
/// but only if they are cheap, so this is built around three rules.
/// </para>
/// <list type="number">
/// <item>
/// <b>The decoder is asked for the size, not for the photograph.</b> A cover cell is under a
/// hundred pixels, and <see cref="Bitmap.DecodeToWidth"/> stops the JPEG decoder at that
/// scale instead of producing a twenty-megapixel image and throwing it away. The reduced-copy
/// service cannot be used here for exactly that reason: ImageSharp decodes the original whole,
/// which is about 96 MB per frame.
/// </item>
/// <item>
/// <b>Two at a time.</b> Previews must never be the reason the window stops answering, so the
/// gate is narrower than the machine: the point of a preview is to be ready a moment later,
/// not to fight the rest of the program for every core.
/// </item>
/// <item>
/// <b>The disk keeps them.</b> A folder that was opened once is not read again on the next
/// opening - the reduced picture is already in the cache. The name carries the source's size
/// and write time, so a photograph that changed is decoded again instead of shown as its old
/// self, and the oldest entries are dropped once the cache has grown past its limit.
/// </item>
/// </list>
/// <para>
/// The directory is under the temporary folder, beside the reduced copies: it is a cache, and
/// deleting it costs time rather than correctness.
/// </para>
/// </remarks>
internal static class FolderPreviewCache
{
    /// <summary>The width a cover cell is decoded at. Under 100 drawn pixels, at up to 150%.</summary>
    private const int PixelWidth = 128;

    /// <summary>Past this many files the cache drops the oldest half of itself.</summary>
    private const int MaxFiles = 3000;

    private const int KeepFiles = 1500;

    /// <summary>Decoded pictures held in memory. About 20 MB, and the disk has the rest.</summary>
    private const int MaxInMemory = 400;

    private static readonly SemaphoreSlim Gate = new(2, 2);

    private static readonly ConcurrentDictionary<string, Bitmap> InMemory =
        new(StringComparer.OrdinalIgnoreCase);

    private static int _pruned;

    private static string CacheDirectory =>
        Path.Combine(Path.GetTempPath(), "PhotoBookRenamer", "FolderPreviews");

    /// <summary>
    /// The first photographs of <paramref name="folder"/>, decoded for a cover.
    /// </summary>
    /// <remarks>
    /// Fewer pictures than asked for is normal and is not an error: a folder may hold two
    /// photographs, or one of them may not decode. The caller draws what it gets.
    /// </remarks>
    public static async Task<IReadOnlyList<Bitmap>> GetAsync(
        string folder,
        int count,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(folder)) return Array.Empty<Bitmap>();

        PruneOnce();

        int wanted = Math.Clamp(count, 1, 8);

        // Listing a folder is disk work: it belongs off the thread that draws.
        var photos = await Task.Run(() => FirstPhotos(folder, wanted), cancellationToken)
            .ConfigureAwait(false);

        var pictures = new List<Bitmap>(photos.Count);
        foreach (var photo in photos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var picture = await GetOneAsync(photo, cancellationToken).ConfigureAwait(false);
            if (picture is not null) pictures.Add(picture);
        }

        return pictures;
    }

    /// <summary>
    /// Fails when a picture decodes - a locked file, a damaged JPEG - and never when the cache
    /// does: a cover without a photograph is a cover with the folder mark on it.
    /// </summary>
    private static async Task<Bitmap?> GetOneAsync(string photo, CancellationToken cancellationToken)
    {
        string key = Key(photo);
        if (InMemory.TryGetValue(key, out var known)) return known;

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (InMemory.TryGetValue(key, out known)) return known;

            string cached = Path.Combine(CacheDirectory, key + ".png");

            var picture = await Task.Run(() =>
            {
                Directory.CreateDirectory(CacheDirectory);

                if (File.Exists(cached)) return Load(cached);

                var decoded = Decode(photo);
                if (decoded is not null) Save(decoded, cached);
                return decoded;
            }, cancellationToken).ConfigureAwait(false);

            if (picture is null) return null;

            if (InMemory.Count >= MaxInMemory) Shed();
            InMemory[key] = picture;

            return picture;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// The name of a cached picture: the source, its size and the moment it was written.
    /// </summary>
    /// <remarks>
    /// A preview that ignores the source would show a photograph the client replaced months
    /// ago. Sixteen characters of a hash is enough for a cache and short enough for a path.
    /// </remarks>
    private static string Key(string photo)
    {
        var info = new FileInfo(photo);
        string identity = $"{photo}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    private static List<string> FirstPhotos(string folder, int count)
    {
        try
        {
            return Directory.EnumerateFiles(folder)
                .Where(IsJpeg)
                .OrderBy(file => file, StringComparer.CurrentCultureIgnoreCase)
                .Take(count)
                .ToList();
        }
        catch (Exception ex)
        {
            Journal.Write(JournalLevel.Information, "folders", "Папка не прочиталась для превью", ex);
            return new List<string>();
        }
    }

    private static bool IsJpeg(string file) =>
        file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
        file.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

    private static Bitmap? Decode(string photo)
    {
        try
        {
            using var stream = File.OpenRead(photo);
            return Bitmap.DecodeToWidth(stream, PixelWidth, BitmapInterpolationMode.HighQuality);
        }
        catch (Exception ex)
        {
            Journal.Write(
                JournalLevel.Information,
                "folders",
                $"Превью не строится: {Path.GetFileName(photo)}",
                ex);
            return null;
        }
    }

    private static Bitmap? Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            Journal.Write(
                JournalLevel.Information,
                "folders",
                "Кэш превью папки не прочитался, файл будет собран заново",
                ex);

            try { File.Delete(path); } catch { }
            return null;
        }
    }

    /// <summary>Written beside the target and moved over it, so a half-written file is never read.</summary>
    private static void Save(Bitmap picture, string path)
    {
        try
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = File.Create(temporary)) picture.Save(stream);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Journal.Write(JournalLevel.Warning, "folders", "Превью папки не сохранилось в кэш", ex);
        }
    }

    private static void Shed()
    {
        foreach (var key in InMemory.Keys.Take(InMemory.Count / 4))
            InMemory.TryRemove(key, out _);
    }

    /// <summary>
    /// Drops the oldest entries once, in the background, the first time a preview is asked for.
    /// </summary>
    /// <remarks>
    /// It runs beside the first opening rather than at start-up: a client who never opens the
    /// folder panel never pays for its cache.
    /// </remarks>
    private static void PruneOnce()
    {
        if (Interlocked.Exchange(ref _pruned, 1) != 0) return;

        _ = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(CacheDirectory);

                var files = new DirectoryInfo(CacheDirectory).GetFiles("*.png");
                if (files.Length <= MaxFiles) return;

                foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc).Take(files.Length - KeepFiles))
                {
                    try { file.Delete(); } catch { }
                }
            }
            catch (Exception ex)
            {
                Journal.Write(JournalLevel.Warning, "folders", "Кэш превью папок не почистился", ex);
            }
        });
    }
}
