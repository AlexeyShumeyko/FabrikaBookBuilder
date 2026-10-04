using PhotoBookRenamer.Application;
using PhotoBook.Core;
using PhotoBookRenamer.Infrastructure;

namespace PhotoBookRenamer.Application.Tests;

/// <summary>
/// Builds the smallest project shape a test needs, so the tests read as statements about
/// behaviour instead of as object graphs.
/// </summary>
internal static class TestProject
{
    /// <summary>One page of a book.</summary>
    public static Page Page(string sourcePath, int index, bool isCover = false) => new()
    {
        SourcePath = sourcePath,
        FileName = Path.GetFileName(sourcePath),
        Index = index,
        DisplayIndex = index,
        IsCover = isCover
    };

    /// <summary>
    /// A book whose cover is <paramref name="coverPath"/> and whose spreads are
    /// <paramref name="spreadPaths"/>. An empty spread list gives a book with a cover only.
    /// </summary>
    public static Book Book(int bookIndex, string coverPath, params string[] spreadPaths)
    {
        var book = new Book
        {
            BookIndex = bookIndex,
            Name = $"Book {bookIndex}",
            FolderPath = $"/books/book{bookIndex}",
            Cover = Page(coverPath, 0, isCover: true)
        };

        for (int i = 0; i < spreadPaths.Length; i++)
        {
            book.Pages.Add(Page(spreadPaths[i], i + 1));
        }

        return book;
    }

    public static Project Combined(params Book[] books)
    {
        var project = new Project { Mode = AppMode.Combined };
        foreach (var book in books) project.Books.Add(book);
        return project;
    }

    public static Project Folders(params Book[] books)
    {
        var project = new Project { Mode = AppMode.UniqueFolders };
        foreach (var book in books) project.Books.Add(book);
        return project;
    }

    /// <summary>
    /// The export service with the real file layer behind it. These tests are about files
    /// and names, so a stand-in for the file layer would test the stand-in instead.
    /// </summary>
    public static ExportService Service() => new(new FileService());
}
