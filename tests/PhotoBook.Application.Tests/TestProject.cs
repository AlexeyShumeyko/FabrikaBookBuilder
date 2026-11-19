using PhotoBook.Application;
using PhotoBook.Core;
using PhotoBook.Infrastructure;

namespace PhotoBook.Application.Tests;

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
    public static Book Book(int bookIndex, string coverPath, params string[] spreadPaths) =>
        Book(bookIndex, null, coverPath, spreadPaths);

    /// <summary>
    /// The same book, with the pixel size its spreads were exported at.
    /// </summary>
    /// <remarks>
    /// The size is what tells two formats of one project apart: a project assembled from two
    /// folders carries one stored format on both books, while the page sizes cannot agree.
    /// </remarks>
    public static Book Book(int bookIndex, (int Width, int Height)? pageSize, string coverPath, params string[] spreadPaths)
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
            var page = Page(spreadPaths[i], i + 1);

            if (pageSize is { } size)
            {
                page.ImageWidth = size.Width;
                page.ImageHeight = size.Height;
            }

            book.Pages.Add(page);
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
