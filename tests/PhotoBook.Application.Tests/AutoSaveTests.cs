using PhotoBook.Application;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Application.Tests;

/// <summary>
/// The mechanism that replaced the save button: a change is written a moment later, a
/// burst of changes is one write, and leaving the screen flushes what is pending.
/// </summary>
public class AutoSaveTests
{
    [Fact]
    public async Task A_burst_of_changes_is_one_write()
    {
        int writes = 0;
        using var auto = new AutoSave(
            new ImmediateUiThread(),
            () =>
            {
                Interlocked.Increment(ref writes);
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(80));

        for (int i = 0; i < 5; i++) auto.Request();

        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref writes) == 1, 3000),
            "the burst was never written");

        await Task.Delay(250);
        Assert.Equal(1, Volatile.Read(ref writes));
    }

    [Fact]
    public async Task Flush_writes_without_waiting_for_the_quiet_period()
    {
        int writes = 0;
        using var auto = new AutoSave(
            new ImmediateUiThread(),
            () =>
            {
                Interlocked.Increment(ref writes);
                return Task.CompletedTask;
            },
            TimeSpan.FromSeconds(30));

        auto.Request();

        // The timer would not fire for thirty seconds; leaving the screen still writes.
        Assert.True(await auto.FlushAsync());
        Assert.Equal(1, Volatile.Read(ref writes));
    }

    [Fact]
    public async Task Flush_with_nothing_pending_writes_nothing()
    {
        int writes = 0;
        using var auto = new AutoSave(
            new ImmediateUiThread(),
            () =>
            {
                Interlocked.Increment(ref writes);
                return Task.CompletedTask;
            });

        Assert.False(await auto.FlushAsync());
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task A_pending_write_is_dropped_when_the_project_is_replaced()
    {
        int writes = 0;
        using var auto = new AutoSave(
            new ImmediateUiThread(),
            () =>
            {
                Interlocked.Increment(ref writes);
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(80));

        auto.Request();
        auto.Cancel();

        await Task.Delay(250);
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task A_failed_write_does_not_stop_the_next_one()
    {
        int attempts = 0;
        using var auto = new AutoSave(
            new ImmediateUiThread(),
            () =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                    throw new IOException("the disk is full");

                return Task.CompletedTask;
            });

        auto.Request();
        await Assert.ThrowsAsync<IOException>(() => auto.FlushAsync());

        auto.Request();
        Assert.True(await auto.FlushAsync());
        Assert.Equal(2, attempts);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);

        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }

        return condition();
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
