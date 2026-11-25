using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Application.Tests;

/// <summary>
/// The two counters of the combined control panel: one press is one book or one spread, the
/// number stops at the range the fields accept, and the line under the first-run card follows it.
/// </summary>
public class CombinedModeRunCounterTests
{
    [Fact]
    public void A_press_of_the_plus_is_one_more_and_the_hint_follows()
    {
        var model = CreateModel();

        Assert.Equal(1, model.NumberOfBooks);
        Assert.Equal("1 книга · 1 разворот · черновик", model.StarterHint);

        model.IncreaseBooksCommand.Execute(null);

        Assert.Equal(2, model.NumberOfBooks);
        Assert.Equal("2", model.NumberOfBooksText);
        Assert.Equal("2 книги · 1 разворот · черновик", model.StarterHint);

        model.IncreaseSpreadsCommand.Execute(null);

        Assert.Equal("2 книги · 2 разворота · черновик", model.StarterHint);
    }

    [Fact]
    public void The_minus_stops_at_one_book_and_one_spread()
    {
        var model = CreateModel();

        Assert.Equal(1, model.NumberOfBooks);
        Assert.Equal(1, model.SpreadsPerBook);
        Assert.False(model.DecreaseBooksCommand.CanExecute(null));
        Assert.False(model.DecreaseSpreadsCommand.CanExecute(null));

        model.IncreaseBooksCommand.Execute(null);
        Assert.True(model.DecreaseBooksCommand.CanExecute(null));

        model.DecreaseBooksCommand.Execute(null);

        Assert.Equal(1, model.NumberOfBooks);
        Assert.False(model.DecreaseBooksCommand.CanExecute(null));
    }

    [Fact]
    public void The_plus_stops_at_the_highest_number_the_box_accepts()
    {
        var model = CreateModel();

        for (int press = 0; press < 120; press++) model.IncreaseBooksCommand.Execute(null);

        Assert.Equal(99, model.NumberOfBooks);
        Assert.False(model.IncreaseBooksCommand.CanExecute(null));
    }

    private static CombinedModeViewModel CreateModel()
        => new(
            fileService: null!,
            files: null!,
            imageService: null!,
            thumbnails: null!,
            exportService: null!,
            loggingService: null!,
            projects: null!,
            projectListService: null!,
            dialogs: null!,
            images: new NullImageCache(),
            ui: new ImmediateUiThread(),
            editorDialogs: null!);

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
