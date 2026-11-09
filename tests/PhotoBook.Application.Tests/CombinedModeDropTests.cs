using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Core;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Application.Tests;

/// <summary>
/// Dropping a photograph into a slot that already has one. The slot must take the new
/// file, forget the old file's size and reduced copy, and re-shape the run from the new
/// measurement - the three things that made a replacement keep showing the old photo.
/// </summary>
public class CombinedModeDropTests
{
    [Fact]
    public void A_drop_replaces_a_filled_slot_and_measures_the_new_photo()
    {
        var spread = new Page
        {
            SourcePath = "/photos/old.jpg",
            ThumbnailPath = "/thumbs/old_thumb.jpg",
            ImageWidth = 4000,
            ImageHeight = 3000,
            Index = 1,
            DisplayIndex = 1
        };

        var book = new Book
        {
            BookIndex = 1,
            Cover = new Page
            {
                IsCover = true,
                Index = 0,
                SourcePath = "/photos/cover.jpg",
                ImageWidth = 5480,
                ImageHeight = 3898
            }
        };
        book.Pages.Add(spread);
        book.UpdatePageSlots();

        var images = new ControlledImageService();
        var thumbnails = new ControlledThumbnailProvider();
        var model = CreateModel(images, thumbnails);
        model.Books.Add(book);

        model.DropFileOnSlot(spread, "/photos/new.jpg", DropAction.ThisBookOnly);

        // The file changed immediately. The old size and the old reduced copy belong to the
        // old photograph and are gone before the new file has been read.
        Assert.Equal("/photos/new.jpg", spread.SourcePath);
        Assert.False(spread.HasDimensions);
        Assert.Null(spread.ThumbnailPath);

        images.Complete(2457, 3602);
        Assert.True(SpinWait.SpinUntil(() => spread.HasDimensions, 3000));
        Assert.Equal(2457, spread.ImageWidth);
        Assert.Equal(3602, spread.ImageHeight);

        // One measured narrow spread decides the whole run; the cover does not take part.
        Assert.Equal(0.682, spread.FrameAspect, 3);

        thumbnails.Complete("/thumbs/new_thumb.jpg");
        Assert.True(SpinWait.SpinUntil(
            () => spread.ThumbnailPath == "/thumbs/new_thumb.jpg", 3000));
    }

    [Fact]
    public void Deleting_a_wide_spread_returns_the_run_to_the_remaining_format()
    {
        var wide = new Page
        {
            SourcePath = "/photos/wide.jpg",
            ThumbnailPath = "/thumbs/wide_thumb.jpg",
            ImageWidth = 4724,
            ImageHeight = 2398,
            Index = 1,
            DisplayIndex = 1
        };
        var narrow = new Page
        {
            SourcePath = "/photos/narrow.jpg",
            ImageWidth = 2457,
            ImageHeight = 3602,
            Index = 2,
            DisplayIndex = 2
        };

        var book = new Book
        {
            BookIndex = 1,
            Cover = new Page { IsCover = true, Index = 0 }
        };
        book.Pages.Add(wide);
        book.Pages.Add(narrow);
        book.UpdatePageSlots();

        var model = CreateModel(new ControlledImageService(), new ControlledThumbnailProvider());
        model.Books.Add(book);

        // The run is a 1:1 tie, so the first spread holds the shape. It is a wide one,
        // clamped to the widest frame the design allows.
        Assert.Equal(1.90, narrow.FrameAspect, 2);

        model.DeletePageCommand.Execute(wide);

        // Nothing of the removed file survives on the slot, and the run follows the
        // narrow spread that is left instead of staying on the deleted format.
        Assert.True(wide.IsEmpty);
        Assert.False(wide.HasDimensions);
        Assert.Null(wide.ThumbnailPath);
        Assert.Equal(0.682, narrow.FrameAspect, 3);
    }

    private static CombinedModeViewModel CreateModel(
        IImageService images, IThumbnailProvider thumbnails)
        => new(
            fileService: null!,
            files: null!,
            imageService: images,
            thumbnails: thumbnails,
            exportService: null!,
            loggingService: null!,
            projects: null!,
            projectListService: null!,
            dialogs: null!,
            images: new NullImageCache(),
            ui: new ImmediateUiThread(),
            editorDialogs: null!);

    private sealed class ControlledImageService : IImageService
    {
        private readonly TaskCompletionSource<(int Width, int Height)> _dimensions =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(int width, int height) => _dimensions.SetResult((width, height));

        public Task<(int Width, int Height)> GetImageDimensionsAsync(string filePath)
            => _dimensions.Task;

        public Task<string?> DetectCoverAsync(string[] filePaths)
            => Task.FromResult<string?>(null);
    }

    private sealed class ControlledThumbnailProvider : IThumbnailProvider
    {
        private readonly TaskCompletionSource<string?> _thumbnail =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(string? path) => _thumbnail.SetResult(path);

        public string? GetExistingPath(string sourcePath) => null;

        public Task<string?> GetOrCreateAsync(
            string sourcePath, CancellationToken cancellationToken = default)
            => _thumbnail.Task;

        public Task EnsureAsync(
            IReadOnlyCollection<string> sourcePaths,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NullImageCache : IDecodedImageCache
    {
        public void Invalidate(DecodedImageKind kind) { }
        public void InvalidateAll() { }
        public void InvalidateFile(string filePath) { }
        public Task PrewarmAsync(
            IEnumerable<string>? filePaths, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class ImmediateUiThread : IUiThread
    {
        public bool IsOnUiThread => true;
        public void Invoke(Action action) => action();
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public void BeginInvoke(Action action, UiDispatchPriority priority = UiDispatchPriority.Normal)
            => action();
    }
}
