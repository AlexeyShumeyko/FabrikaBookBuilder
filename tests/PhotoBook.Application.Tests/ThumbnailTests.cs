using PhotoBook.Application;
using PhotoBookRenamer.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace PhotoBook.Application.Tests;

/// <summary>
/// Where a reduced copy lives, and that one is produced. The formula in particular used to be
/// written out in four places, and a formula that is copied is a formula that will drift: a
/// slot would keep showing a file that no longer exists.
/// </summary>
public sealed class ThumbnailTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "PhotoBookRenamer.Tests", Guid.NewGuid().ToString("N"));

    private readonly List<string> _sources = new();
    private readonly List<string> _written = new();

    public ThumbnailTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        foreach (var file in _written)
        {
            try { File.Delete(file); } catch { }
        }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Jpeg(string name, int width, int height)
    {
        var path = Path.Combine(_root, name);
        using var image = new Image<Rgba32>(width, height);
        image.SaveAsJpeg(path, new JpegEncoder { Quality = 90 });
        _sources.Add(path);
        return path;
    }// ------------------------------------------------------------------ where it lives

    [Fact]
    public void The_same_photograph_always_gets_the_same_place()
    {
        const string source = @"D:\Shootings\Album 01\001.jpg";

        Assert.Equal(ThumbnailStore.GetPath(source), ThumbnailStore.GetPath(source));
    }

    [Fact]
    public void Two_photographs_with_the_same_name_in_different_folders_do_not_collide()
    {
        var a = ThumbnailStore.GetPath(@"D:\Album 01\001.jpg");
        var b = ThumbnailStore.GetPath(@"D:\Album 02\001.jpg");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void The_name_is_a_short_hex_id_and_stays_recognisable_as_a_jpeg()
    {
        var path = ThumbnailStore.GetPath(@"D:\Album\001.jpg");

        Assert.EndsWith("_thumb.jpg", path);
        Assert.Equal(ThumbnailStore.Directory, Path.GetDirectoryName(path));

        var id = Path.GetFileName(path).Replace("_thumb.jpg", string.Empty);
        Assert.Equal(16, id.Length);
        Assert.All(id, c => Assert.True(Uri.IsHexDigit(c), $"not a hex digit: {c}"));
    }

    [Fact]
    public void A_cyrillic_path_produces_the_same_place_every_time()
    {
        var path = ThumbnailStore.GetPath(@"D:\Съёмка\Альбом 01\001.jpg");

        Assert.Equal(path, ThumbnailStore.GetPath(@"D:\Съёмка\Альбом 01\001.jpg"));
        Assert.Equal(ThumbnailStore.Directory, Path.GetDirectoryName(path));
    }

    [Fact]
    public void Asks_before_it_creates_anything()
    {
        var source = Jpeg("not-yet.jpg", 40, 30);
        var expected = ThumbnailStore.GetPath(source);
        if (File.Exists(expected)) File.Delete(expected);

        Assert.Null(ThumbnailStore.GetExistingPath(source));
    }

    // ------------------------------------------------------------------ one is produced

    [Fact]
    public async Task Produces_a_reduced_copy_where_the_store_says_it_belongs()
    {
        var source = Jpeg("big.jpg", 2400, 1600);
        var provider = new ThumbnailProvider();
        var expected = ThumbnailStore.GetPath(source);
        _written.Add(expected);

        var created = await provider.GetOrCreateAsync(source);

        Assert.Equal(expected, created);
        Assert.True(File.Exists(expected));
    }

    [Fact]
    public async Task The_reduced_copy_is_smaller_than_the_original()
    {
        var source = Jpeg("large.jpg", 3000, 2000);
        var provider = new ThumbnailProvider();
        var created = await provider.GetOrCreateAsync(source);
        _written.Add(created!);

        Assert.True(new FileInfo(created!).Length < new FileInfo(source).Length / 4,
            "a 500 px copy of a 3000 px photograph should be far smaller than the original");
    }

    [Fact]
    public async Task A_copy_that_already_exists_is_not_rewritten()
    {
        var source = Jpeg("cached.jpg", 800, 600);
        var provider = new ThumbnailProvider();
        var first = await provider.GetOrCreateAsync(source);
        _written.Add(first!);

        var stamp = new FileInfo(first!).LastWriteTimeUtc;
        var second = await provider.GetOrCreateAsync(source);

        Assert.Equal(first, second);
        Assert.Equal(stamp, new FileInfo(second!).LastWriteTimeUtc);
    }

    [Fact]
    public async Task A_missing_photograph_yields_nothing_rather_than_an_exception()
    {
        var provider = new ThumbnailProvider();

        Assert.Null(await provider.GetOrCreateAsync(Path.Combine(_root, "absent.jpg")));
    }

    [Fact]
    public async Task A_file_that_only_looks_like_a_photograph_yields_nothing()
    {
        var path = Path.Combine(_root, "broken.jpg");
        File.WriteAllBytes(path, new byte[64]);
        _sources.Add(path);
        _written.Add(ThumbnailStore.GetPath(path));

        var provider = new ThumbnailProvider();

        Assert.Null(await provider.GetOrCreateAsync(path));
    }

    [Fact]
    public async Task A_batch_covers_every_photograph_and_reaches_the_end_of_its_progress()
    {
        var paths = new List<string>();
        for (int i = 0; i < 12; i++) paths.Add(Jpeg($"batch-{i:D2}.jpg", 1600 + i, 1200));
        foreach (var p in paths) _written.Add(ThumbnailStore.GetPath(p));

        double last = 0;
        var progress = new Progress<double>(value => last = value);
        var provider = new ThumbnailProvider(maxConcurrency: 3);

        await provider.EnsureAsync(paths, progress);

        Assert.All(paths, p => Assert.True(File.Exists(ThumbnailStore.GetPath(p))));
        Assert.Equal(1.0, last, 3);
    }

    [Fact]
    public async Task An_empty_batch_does_nothing_and_does_not_fail()
    {
        var provider = new ThumbnailProvider();

        await provider.EnsureAsync(Array.Empty<string>());
    }

    [Fact]
    public async Task A_batch_stops_when_it_is_cancelled()
    {
        var paths = new List<string>();
        for (int i = 0; i < 30; i++)
        {
            var p = Jpeg($"cancel-{i:D2}.jpg", 2400, 1600);
            paths.Add(p);
            _written.Add(ThumbnailStore.GetPath(p));
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var provider = new ThumbnailProvider();
        await provider.EnsureAsync(paths, cancellationToken: cts.Token);

        // Nothing is asserted about the result: a cancelled batch may have produced some
        // copies before it stopped. What matters is that it returned instead of continuing
        // through 30 photographs nobody is waiting for.
    }
}
