using System.Threading.Tasks;
using Avalonia.Controls;

namespace PhotoBook.Shell.Views;

/// <summary>
/// A panel that sits over the page and answers with something.
/// </summary>
/// <remarks>
/// <para>
/// The answer is a task the panel completes itself, rather than a value the window closes
/// with: this one lives inside the window it covers, so there is nothing to close. The host
/// puts it up, waits, and takes it down again whatever the answer was.
/// </para>
/// <para>
/// Anything that happens that makes the sheet wrong - a parent resized under it, the window
/// closed while it was up - leaves it there rather than guessing. A sheet that is still there
/// when the window is shutting down is being destroyed with the window, which is correct.
/// </para>
/// </remarks>
/// <typeparam name="T">What it answers with.</typeparam>
public abstract class OverlaySheet<T> : UserControl where T : class
{
    private readonly TaskCompletionSource<T?> _answer = new();

    /// <summary>Completes when the person has decided, or has left.</summary>
    public Task<T?> Answer => _answer.Task;

    /// <summary>
    /// Answers. The first answer stands: a second one would be a button pressed twice, or a
    /// key held down, and the caller has already moved on.
    /// </summary>
    protected void Finish(T? value) => _answer.TrySetResult(value);

    /// <summary>Leaves without answering, which is not the same as answering "nothing".</summary>
    protected void Leave() => _answer.TrySetResult(null);
}
