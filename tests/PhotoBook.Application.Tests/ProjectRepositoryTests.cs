using PhotoBook.Core;
using PhotoBookRenamer.Application;
using PhotoBookRenamer.Infrastructure;

namespace PhotoBookRenamer.Application.Tests;

/// <summary>
/// The project file as storage: what a person is told when it cannot be written, and what
/// happens to a file written before photographs carried their pixel size.
///
/// The compatibility of the format itself is covered by ProjectCompatibilityTests against a
/// real 1.1.1 file; these are the decisions around it.
/// </summary>
public class ProjectRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "PhotoBookRenamer.Tests", Guid.NewGuid().ToString("N"));

    private readonly FakeImageService _images = new();

    public ProjectRepositoryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private IProjectRepository Repository() => new ProjectRepository(_images);

    private string At(string name) => Path.Combine(_root, name);

    private async Task WriteAsync(string name, string content)
    {
        var path = At(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    /// <summary>
    /// A project in the shape 1.1.1 wrote before sizes were stored: no imageWidth at all.
    /// </summary>
    private const string OldProject = """
    {
      "mode": 2,
      "outputFolder": "D:\\Out",
      "books": [
        {
          "folderPath": "D:\\Album-01",
          "name": "Album 01",
          "bookIndex": 1,
          "cover": {
            "sourcePath": "D:\\Album-01\\000-cover.jpg",
            "isCover": true,
            "index": 0,
            "displayIndex": 0
          },
          "pages": [
            {
              "sourcePath": "D:\\Album-01\\001.jpg",
              "isCover": false,
              "index": 1,
              "displayIndex": 1
            },
            {
              "sourcePath": "D:\\Album-01\\002.jpg",
              "isCover": false,
              "index": 2,
              "displayIndex": 2
            }
          ]
        }
      ],
      "availableFiles": ["D:\\Album-01\\003.jpg"]
    }
    """;

    /// <summary>A project with one book, cover only - the smallest thing worth storing.</summary>
    private static Project OneBookProject() =>
        TestProject.Folders(TestProject.Book(1, @"D:\Album-01\000-cover.jpg"));

    [Fact]
    public async Task A_project_that_was_never_saved_answers_null()
    {
        // Not an error: a project that has not been saved yet simply is not on disk.
        Assert.Null(await Repository().LoadAsync(At("nothing-here.json")));
        Assert.Empty(_images.Asked);
    }

    [Fact]
    public async Task An_empty_file_answers_null()
    {
        await WriteAsync("empty.json", string.Empty);

        Assert.Null(await Repository().LoadAsync(At("empty.json")));
    }

    [Fact]
    public async Task A_file_that_is_not_a_project_says_so_in_words_a_person_can_read()
    {
        await WriteAsync("broken.json", "this is not a project");

        // The message reaches the window as-is, so it has to name the operation that failed.
        var error = await Assert.ThrowsAsync<Exception>(() => Repository().LoadAsync(At("broken.json")));

        Assert.StartsWith("Ошибка загрузки проекта", error.Message);
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public async Task Saving_creates_the_folder_when_it_is_not_there_yet()
    {
        var path = At(Path.Combine("new", "deeper", "project.json"));

        await Repository().SaveAsync(OneBookProject(), path);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task A_save_that_cannot_happen_says_so_in_words_a_person_can_read()
    {
        // A file where the folder should be: the most common reason a save fails on a
        // desktop, and the one where silence would lose the work.
        await WriteAsync("blocker", "not a folder");

        var error = await Assert.ThrowsAsync<Exception>(
            () => Repository().SaveAsync(OneBookProject(), At(Path.Combine("blocker", "project.json"))));

        Assert.StartsWith("Ошибка сохранения проекта", error.Message);
    }

    [Fact]
    public async Task A_project_comes_back_with_its_mode_output_folder_and_file_list()
    {
        var original = OneBookProject();
        original.OutputFolder = @"D:\Out";
        original.AvailableFiles.Add(@"D:\Album-01\003.jpg");

        await Repository().SaveAsync(original, At("round-trip.json"));
        var loaded = await Repository().LoadAsync(At("round-trip.json"));

        Assert.NotNull(loaded);
        Assert.Equal(AppMode.UniqueFolders, loaded!.Mode);
        Assert.Equal(@"D:\Out", loaded.OutputFolder);
        Assert.Equal(new[] { @"D:\Album-01\003.jpg" }, loaded.AvailableFiles);
        Assert.Equal(original.Books[0].Name, loaded.Books[0].Name);
    }

    [Fact]
    public async Task An_old_project_gets_its_photo_sizes_filled_on_first_open()
    {
        await WriteAsync("old.json", OldProject);

        var loaded = await Repository().LoadAsync(At("old.json"));

        Assert.NotNull(loaded);
        var book = loaded!.Books[0];
        Assert.Equal(800, book.Cover!.ImageWidth);
        Assert.Equal(600, book.Cover.ImageHeight);
        Assert.All(book.Pages, p =>
        {
            Assert.Equal(800, p.ImageWidth);
            Assert.Equal(600, p.ImageHeight);
        });
        // Cover and both pages - once each, and nothing more.
        Assert.Equal(3, _images.Asked.Count);
    }

    [Fact]
    public async Task A_project_that_already_knows_its_sizes_is_not_read_from_disk_again()
    {
        // Once the sizes are in the file they are the truth, even if the photographs have
        // moved to another disk since. Paying for the headers again on every open is the
        // thing the stored numbers exist to avoid.
        await WriteAsync("sized.json", OldProject.Replace(
            "\"sourcePath\": \"D:\\\\Album-01\\\\001.jpg\",",
            "\"sourcePath\": \"D:\\\\Album-01\\\\001.jpg\", \"imageWidth\": 1600, \"imageHeight\": 1200,"));

        var loaded = await Repository().LoadAsync(At("sized.json"));

        Assert.Equal(1600, loaded!.Books[0].Pages[0].ImageWidth);
        Assert.Equal(1200, loaded.Books[0].Pages[0].ImageHeight);
        Assert.DoesNotContain(@"D:\Album-01\001.jpg", _images.Asked);
        Assert.Equal(2, _images.Asked.Count); // the cover and the page without sizes
    }

    [Fact]
    public async Task The_stored_file_is_camel_case_and_indented()
    {
        // Pinned because it is the format on other people's disks, not a style choice.
        var path = At("format.json");
        await Repository().SaveAsync(OneBookProject(), path);

        var text = await File.ReadAllTextAsync(path);

        Assert.Contains("\"mode\":", text);
        Assert.Contains("\"outputFolder\":", text);
        Assert.Contains("\"bookIndex\":", text);
        Assert.DoesNotContain("\"Mode\":", text);
        Assert.Contains(Environment.NewLine + "  \"books\"", text);
    }

    /// <summary>
    /// Answers a fixed size and remembers what it was asked, so a test can tell "filled in"
    /// from "left alone".
    /// </summary>
    private sealed class FakeImageService : IImageService
    {
        internal List<string> Asked { get; } = new();

        public Task<(int Width, int Height)> GetImageDimensionsAsync(string filePath)
        {
            Asked.Add(filePath);
            return Task.FromResult((800, 600));
        }

        public Task<string?> DetectCoverAsync(string[] filePaths) => Task.FromResult<string?>(null);
    }
}
