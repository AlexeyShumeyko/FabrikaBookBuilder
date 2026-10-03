using PhotoBookRenamer.Application;
using PhotoBookRenamer.Infrastructure;

namespace PhotoBook.Application.Tests;

/// <summary>
/// The folders mode export, end to end: real files in a temporary directory, real copies,
/// real names on disk. The naming rules are checked by <see cref="ExportFileNameTests"/>;
/// what matters here is that the service produces exactly those names and leaves the
/// originals alone.
/// </summary>
public sealed class FoldersModeExportTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "PhotoBookRenamer.Tests", Guid.NewGuid().ToString("N"));

    private readonly List<string> _sources = new();

    public FoldersModeExportTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* a locked file must not fail a test run */ }
    }

    /// <summary>A file that exists on disk, so the service can really copy it.</summary>
    private string Source(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        _sources.Add(path);
        return path;
    }

    private string OutputFolder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static ExportService Service() => TestProject.Service();

    [Fact]
    public async Task Writes_one_file_per_slot_with_the_book_index_first()
    {
        var cover = Source("cover.jpg");
        var spread = Source("spread.jpg");
        var project = TestProject.Folders(TestProject.Book(1, cover, spread));

        var output = OutputFolder("out");
        Assert.True(await Service().ExportProjectAsync(project, output));

        var files = Directory.GetFiles(output).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "001-00.jpg", "001-01.jpg" }, files);
    }

    [Fact]
    public async Task The_cover_is_slot_zero_and_the_first_spread_is_slot_one()
    {
        var project = TestProject.Folders(
            TestProject.Book(2, Source("c.jpg"), Source("a.jpg"), Source("b.jpg")));

        var output = OutputFolder("out");
        await Service().ExportProjectAsync(project, output);

        Assert.True(File.Exists(Path.Combine(output, "002-00.jpg")));
        Assert.True(File.Exists(Path.Combine(output, "002-01.jpg")));
        Assert.True(File.Exists(Path.Combine(output, "002-02.jpg")));
    }

    [Fact]
    public async Task Empty_slots_produce_no_files()
    {
        var cover = Source("c.jpg");
        var page = TestProject.Page(null, 3);
        var book = TestProject.Book(1, cover);
        book.Pages.Add(page);

        var output = OutputFolder("out");
        await Service().ExportProjectAsync(project: TestProject.Folders(book), output);

        Assert.Equal(new[] { "001-00.jpg" }, Directory.GetFiles(output).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Puts_each_book_in_its_own_subfolder_when_asked()
    {
        var project = TestProject.Folders(
            TestProject.Book(1, Source("c1.jpg"), Source("s1.jpg")),
            TestProject.Book(2, Source("c2.jpg"), Source("s2.jpg")));

        var output = OutputFolder("out");
        var options = new ExportOptions { PerBookSubfolders = true };
        Assert.True(await Service().ExportProjectAsync(project, output, options));

        var folders = Directory.GetDirectories(output).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "001 Book 1", "002 Book 2" }, folders);
        Assert.True(File.Exists(Path.Combine(output, "001 Book 1", "001-00.jpg")));
        Assert.True(File.Exists(Path.Combine(output, "002 Book 2", "002-01.jpg")));
    }

    [Fact]
    public async Task Copies_the_originals_without_changing_them()
    {
        var cover = Source("c.jpg");
        var before = File.ReadAllBytes(cover);

        var output = OutputFolder("out");
        await Service().ExportProjectAsync(TestProject.Folders(TestProject.Book(1, cover)), output);

        Assert.Equal(before, File.ReadAllBytes(cover));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(output, "001-00.jpg")));
    }

    [Fact]
    public async Task Refuses_an_empty_target_and_says_nothing_succeeded()
    {
        var project = TestProject.Folders(TestProject.Book(1, Source("c.jpg")));

        Assert.False(await Service().ExportProjectAsync(project, string.Empty));
        Assert.False(await Service().ExportProjectAsync(project, "   "));
    }

    [Fact]
    public async Task Reports_a_missing_source_instead_of_throwing()
    {
        var project = TestProject.Folders(TestProject.Book(1, Path.Combine(_root, "gone.jpg")));
        var output = OutputFolder("out");

        // The service answers with false: an export that cannot run is an expected outcome
        // here, not an exception the caller has to catch.
        Assert.False(await Service().ExportProjectAsync(project, output));
    }
}