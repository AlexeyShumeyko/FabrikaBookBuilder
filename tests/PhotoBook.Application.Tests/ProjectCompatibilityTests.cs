using PhotoBookRenamer.Application;
using PhotoBook.Core;
using PhotoBookRenamer.Infrastructure;

namespace PhotoBookRenamer.Application.Tests;

/// <summary>
/// A project written by version 1.1.1 has to open in every later version and export the
/// same file names. The file in data\ is a copy of that format, fabricated - no customer
/// paths, no customer photographs - with the awkward parts kept on purpose: Cyrillic names
/// stored as \u escapes, a book with fewer spreads than the first one, an empty slot, and a
/// missing imageWidth that 1.1.1 wrote for older projects.
///
/// If the serializer changes shape, this file stops loading and the test says so. That is
/// the point: the format is a promise to the people who already have projects saved.
/// </summary>
public class ProjectCompatibilityTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "PhotoBookRenamer.Tests", Guid.NewGuid().ToString("N"));

    public ProjectCompatibilityTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static string SamplePath()
    {
        // Copied to the output directory by the project file, so the test reads the same
        // bytes anywhere.
        var path = Path.Combine(AppContext.BaseDirectory, "data", "project-1.1.1.json");
        Assert.True(File.Exists(path), $"the sample project is missing: {path}");
        return path;
    }

    private static ProjectService Service() => new(new FileService(), new ImageService());

    [Fact]
    public async Task Opens_a_project_written_by_1_1_1()
    {
        var project = await Service().LoadProjectAsync(SamplePath());

        Assert.NotNull(project);
        Assert.Equal(AppMode.UniqueFolders, project!.Mode);
        Assert.Equal(2, project.Books.Count);
    }

    [Fact]
    public async Task Reads_cyrillic_names_written_as_unicode_escapes()
    {
        var project = await Service().LoadProjectAsync(SamplePath());

        Assert.Equal("Альбом 01", project!.Books[0].Name);
        Assert.Equal("Альбом 02", project.Books[1].Name);
    }

    [Fact]
    public async Task Keeps_the_cover_out_of_the_spreads()
    {
        var project = await Service().LoadProjectAsync(SamplePath());
        var book = project!.Books[0];

        Assert.Equal("000-cover.jpg", Path.GetFileName(book.Cover!.SourcePath!));
        Assert.All(book.Pages, p => Assert.False(p.IsCover));
        Assert.DoesNotContain(book.Pages, p => p.SourcePath == book.Cover!.SourcePath);
    }

    [Fact]
    public async Task Keeps_an_empty_slot_as_an_empty_slot()
    {
        var project = await Service().LoadProjectAsync(SamplePath());
        var second = project!.Books[1];

        Assert.Equal(2, second.Pages.Count);
        Assert.True(second.Pages[1].IsEmpty);
        Assert.Equal(2, second.Pages[1].Index);
    }

    [Fact]
    public async Task Keeps_the_books_in_their_order()
    {
        var project = await Service().LoadProjectAsync(SamplePath());

        Assert.Equal(1, project!.Books[0].BookIndex);
        Assert.Equal(2, project.Books[1].BookIndex);
    }

    [Fact]
    public async Task A_project_survives_a_save_and_a_reload_unchanged()
    {
        var loaded = await Service().LoadProjectAsync(SamplePath());
        Assert.NotNull(loaded);

        var saved = Path.Combine(_root, "saved.json");
        await Service().SaveProjectAsync(loaded!, saved);
        var reloaded = await Service().LoadProjectAsync(saved);

        Assert.NotNull(reloaded);
        Assert.Equal(loaded!.Books.Count, reloaded!.Books.Count);
        for (int b = 0; b < loaded.Books.Count; b++)
        {
            Assert.Equal(loaded.Books[b].Name, reloaded.Books[b].Name);
            Assert.Equal(loaded.Books[b].BookIndex, reloaded.Books[b].BookIndex);
            Assert.Equal(loaded.Books[b].Pages.Count, reloaded.Books[b].Pages.Count);
            Assert.Equal(
                loaded.Books[b].Pages.Select(p => p.SourcePath),
                reloaded.Books[b].Pages.Select(p => p.SourcePath));
        }
    }

    [Fact]
    public async Task Exports_a_1_1_1_project_with_the_contract_names()
    {
        // The sample points at files that do not exist here, so the sources are rewritten to
        // files that do. The names are decided by the loaded project, not by the rewrite.
        var project = await Service().LoadProjectAsync(SamplePath());
        Assert.NotNull(project);

        var sources = new Dictionary<string, string>();
        foreach (var book in project!.Books)
        {
            foreach (var page in book.Pages.Append(book.Cover!))
            {
                if (page.SourcePath is null) continue;
                var name = $"{sources.Count:D2}-{Path.GetFileName(page.SourcePath)}";
                var path = Path.Combine(_root, name);
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                sources[page.SourcePath] = path;
            }
        }

        foreach (var book in project.Books)
        {
            if (book.Cover?.SourcePath is { } coverSource && sources.TryGetValue(coverSource, out var coverPath))
                book.Cover.SourcePath = coverPath;
            foreach (var page in book.Pages)
            {
                if (page.SourcePath is { } source && sources.TryGetValue(source, out var target))
                    page.SourcePath = target;
            }
        }

        var output = Path.Combine(_root, "out");
        Directory.CreateDirectory(output);
        Assert.True(await TestProject.Service().ExportProjectAsync(project, output));

        var written = Directory.GetFiles(output).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Equal(
            new[] { "001-00.jpg", "001-01.jpg", "001-02.jpg", "001-03.jpg", "002-00.jpg", "002-01.jpg" },
            written);
    }
}
