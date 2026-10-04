using PhotoBookRenamer.Application;
using PhotoBookRenamer.Infrastructure;

namespace PhotoBookRenamer.Application.Tests;

/// <summary>
/// The update feed, without the network. Two things are worth pinning here because they are
/// what a user notices: the version string the window shows, and the fact that a feed that
/// cannot be reached answers "nothing" instead of throwing. Failing to check for an update
/// must never stop the program from starting.
/// </summary>
public class UpdateFeedTests
{
    private static UpdateService Feed() => new();

    [Fact]
    public void The_version_is_three_numbers()
    {
        var version = Feed().GetCurrentVersion();

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Equal(3, version.Split('.').Length);
        Assert.All(version.Split('.'), part => Assert.True(int.TryParse(part, out _), $"not a number: {part}"));
    }

    [Fact]
    public async Task A_url_that_is_not_an_update_answers_null_rather_than_throwing()
    {
        // The window treats a null answer as "could not update" and offers the button again.
        // An exception here would surface as an unhandled error while the window is closing.
        Assert.Null(await Feed().DownloadInstallerAsync("not-a-url-at-all"));
    }

    [Fact]
    public async Task Asking_for_an_empty_url_answers_null_rather_than_throwing()
    {
        Assert.Null(await Feed().DownloadInstallerAsync(string.Empty));
    }

    [Fact]
    public void The_feed_is_reachable_through_its_port()
    {
        // The registration is what the shell uses; a mismatch here only shows up at runtime,
        // when the window opens on somebody's machine.
        IUpdateFeed feed = Feed();

        Assert.NotNull(feed.GetCurrentVersion());
    }
}