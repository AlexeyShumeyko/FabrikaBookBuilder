using System.Reflection;
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
    public void The_version_is_the_running_program_not_the_assembly_that_asks_for_updates()
    {
        // The feed lives in a library now, and a library is versioned on its own - 1.0.0.0
        // by default. Reading its own assembly instead of the program's made every launch
        // offer an update that was already installed, because 1.0.0 is older than any
        // release. Under the test host the entry assembly is the host, so this asserts the
        // contract rather than a literal number: whatever program is running, the feed
        // reports that program's version.
        var entry = Assembly.GetEntryAssembly();
        Assert.NotNull(entry);

        var expected = entry!.GetName().Version;

        Assert.Equal($"{expected.Major}.{expected.Minor}.{expected.Build}", Feed().GetCurrentVersion());
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
