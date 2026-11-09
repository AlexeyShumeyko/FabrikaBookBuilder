using PhotoBook.Core;

namespace PhotoBook.Application.Tests;

/// <summary>
/// A page's stored size and reduced copy belong to the file it currently points at. Once
/// another file arrives they are not facts about the slot any more.
/// </summary>
public class PageSourceTests
{
    [Fact]
    public void Replacing_the_source_forgets_the_old_size_and_thumbnail()
    {
        var page = new Page
        {
            SourcePath = "/photos/old.jpg",
            ThumbnailPath = "/thumbs/old_thumb.jpg",
            ImageWidth = 2457,
            ImageHeight = 3602,
            FileName = "old.jpg"
        };

        page.SourcePath = "/photos/new.jpg";

        Assert.False(page.HasDimensions);
        Assert.Null(page.ThumbnailPath);
        Assert.Equal("new.jpg", page.FileName);
    }

    [Fact]
    public void Emptying_a_slot_forgets_its_size()
    {
        var page = new Page { SourcePath = "/photos/old.jpg", ImageWidth = 2457, ImageHeight = 3602 };

        page.SourcePath = null;

        Assert.True(page.IsEmpty);
        Assert.False(page.HasDimensions);
    }

    [Fact]
    public void A_copy_keeps_the_size_and_thumbnail_of_the_photo_it_copies()
    {
        var page = new Page
        {
            SourcePath = "/photos/old.jpg",
            ThumbnailPath = "/thumbs/old_thumb.jpg",
            ImageWidth = 2457,
            ImageHeight = 3602,
            FileName = "old.jpg"
        };

        var copy = page.Clone();

        Assert.Equal(page.SourcePath, copy.SourcePath);
        Assert.Equal(page.ThumbnailPath, copy.ThumbnailPath);
        Assert.Equal(page.ImageWidth, copy.ImageWidth);
        Assert.Equal(page.ImageHeight, copy.ImageHeight);
    }
}
