using PhotoBookRenamer.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace PhotoBookRenamer.Application.Tests;

/// <summary>
/// Choosing folders and choosing a cover are the two rules a user hits first: a wrong
/// answer here is a wrong order of pages on the print site. Both run on real files in a
/// temporary directory.
/// </summary>
public sealed class FolderAndCoverRulesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "PhotoBookRenamer.Tests", Guid.NewGuid().ToString("N"));

    public FolderAndCoverRulesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>A file with the extension; content does not matter for the rules below.</summary>
    private static void Touch(string folder, string name, int size = 4) =>
        File.WriteAllBytes(Path.Combine(folder, name), new byte[size]);

    private static void TouchJpeg(string folder, string name, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        image.SaveAsJpeg(Path.Combine(folder, name), new JpegEncoder { Quality = 80 });
    }

    private static FileService Files() => new();

    // ------------------------------------------------------------------ folders

    [Fact]
    public async Task Folders_with_the_same_number_of_photos_are_accepted()
    {
        var a = Folder("a");
        var b = Folder("b");
        foreach (var f in new[] { a, b })
        {
            Touch(f, "01.jpg");
            Touch(f, "02.jpg");
            Touch(f, "03.jpg");
        }

        var result = await Files().ValidateFoldersDetailedAsync(new List<string> { a, b });

        Assert.True(result.IsValid, result.ErrorMessage);
    }

    [Fact]
    public async Task A_folder_with_a_different_number_of_photos_is_rejected_by_name()
    {
        var a = Folder("a");
        var b = Folder("b");
        foreach (var f in new[] { a, b })
        {
            Touch(f, "01.jpg");
            Touch(f, "02.jpg");
            Touch(f, "03.jpg");
        }
        Touch(b, "04.jpg");

        var result = await Files().ValidateFoldersDetailedAsync(new List<string> { a, b });

        Assert.False(result.IsValid);
        Assert.Equal(b, result.ProblemFolder);
        Assert.Contains("b", result.ErrorMessage!);
        Assert.Contains("3", result.ErrorMessage!);
    }

    [Fact]
    public async Task The_rule_follows_the_majority_not_the_first_folder()
    {
        // Three folders with four photos each and one with two: the odd one out is the
        // problem, whichever order they arrive in.
        var many = new[] { Folder("m1"), Folder("m2"), Folder("m3") };
        foreach (var f in many)
        {
            for (int i = 1; i <= 4; i++) Touch(f, $"{i:D2}.jpg");
        }
        var few = Folder("few");
        Touch(few, "01.jpg");
        Touch(few, "02.jpg");

        var folders = new List<string> { few };
        folders.AddRange(many);

        var result = await Files().ValidateFoldersDetailedAsync(folders);

        Assert.False(result.IsValid);
        Assert.Equal(few, result.ProblemFolder);
    }

    [Fact]
    public async Task A_folder_with_no_photos_is_rejected()
    {
        var a = Folder("a");
        Touch(a, "01.jpg");
        var empty = Folder("empty");

        var result = await Files().ValidateFoldersDetailedAsync(new List<string> { a, empty });

        Assert.False(result.IsValid);
        Assert.Equal(empty, result.ProblemFolder);
    }

    [Fact]
    public async Task A_file_that_is_not_a_jpeg_is_rejected()
    {
        var a = Folder("a");
        Touch(a, "01.jpg");
        Touch(a, "notes.txt");
        var b = Folder("b");
        Touch(b, "01.jpg");

        var result = await Files().ValidateFoldersDetailedAsync(new List<string> { a, b });

        Assert.False(result.IsValid);
        Assert.Equal(a, result.ProblemFolder);
    }

    [Fact]
    public async Task A_folder_that_does_not_exist_is_rejected()
    {
        var result = await Files().ValidateFoldersDetailedAsync(
            new List<string> { Path.Combine(_root, "no-such-folder") });

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task No_folders_at_all_is_rejected()
    {
        var result = await Files().ValidateFoldersDetailedAsync(new List<string>());

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Both_capitalisations_of_the_extension_count_as_photos()
    {
        var a = Folder("a");
        Touch(a, "01.JPG");
        Touch(a, "02.jpeg");
        var b = Folder("b");
        Touch(b, "01.jpg");
        Touch(b, "02.jpg");

        var result = await Files().ValidateFoldersDetailedAsync(new List<string> { a, b });

        Assert.True(result.IsValid, result.ErrorMessage);
    }

    // ------------------------------------------------------------------ cover

    [Fact]
    public async Task The_cover_is_the_photograph_with_the_most_pixels()
    {
        var folder = Folder("cover");
        TouchJpeg(folder, "small.jpg", 400, 300);
        TouchJpeg(folder, "wide.jpg", 3000, 1000);
        TouchJpeg(folder, "tall.jpg", 1000, 3000);

        var files = (await Files().GetJpegFilesAsync(folder)).ToArray();
        var cover = await new ImageService().DetectCoverAsync(files);

        Assert.Equal(Path.Combine(folder, "tall.jpg"), cover);
    }

    [Fact]
    public async Task An_empty_folder_has_no_cover()
    {
        var folder = Folder("empty-cover");

        Assert.Null(await new ImageService().DetectCoverAsync(Array.Empty<string>()));
    }

    [Fact]
    public async Task A_photograph_that_is_not_an_image_is_skipped_rather_than_throwing()
    {
        var folder = Folder("broken");
        TouchJpeg(folder, "good.jpg", 800, 600);
        Touch(folder, "broken.jpg", 16);   // right extension, not an image

        var files = (await Files().GetJpegFilesAsync(folder)).ToArray();
        var cover = await new ImageService().DetectCoverAsync(files);

        Assert.Equal(Path.Combine(folder, "good.jpg"), cover);
    }
}
