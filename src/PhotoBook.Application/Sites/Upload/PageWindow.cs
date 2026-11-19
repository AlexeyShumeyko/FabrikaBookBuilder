using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoBook.Application.Sites.Upload
{
    /// <summary>What a window of page sends ended with.</summary>
    /// <param name="Accepted">Which positions the site took, indexed by position.</param>
    /// <param name="Refusal">The first page the site refused, or null when it refused none.</param>
    /// <param name="FailedPosition">The position of that page, or minus one.</param>
    /// <param name="Cancelled">Whether the customer stopped the run.</param>
    internal sealed record PageWindowResult(
        bool[] Accepted,
        PageUpload? Refusal,
        int FailedPosition,
        bool Cancelled);

    /// <summary>Sends the pages of one book with several of them in flight at once.</summary>
    /// <remarks>
    /// <para>
    /// The position travels with every request, so the site orders the pages by what it is told
    /// rather than by whichever request arrives first. That is what makes a window possible:
    /// several files in flight land at their own positions, and the book keeps its order.
    /// </para>
    /// <para>
    /// The window is deliberately below what the platform allows. The site's own uploader keeps
    /// five files in flight; four stays under that and still removes most of the wait for the
    /// site's own processing of each page. One file per round trip paid that processing end to
    /// end, and the old window of two paid it only two pages over.
    /// </para>
    /// <para>
    /// A refusal stops the window rather than the run: the pages already in flight are awaited
    /// and kept, no new one is started, and the first refusal is returned with its position. A
    /// cancelled token ends it the same way, except that a send the site never answered is not
    /// reported as a refusal.
    /// </para>
    /// </remarks>
    internal static class PageWindow
    {
        /// <summary>Files sent at once; the site's own uploader keeps five.</summary>
        public const int MaxInFlight = 4;

        /// <summary>
        /// Sends the pages from <paramref name="first"/> on, calling back as each one starts and
        /// as each one is accepted.
        /// </summary>
        /// <param name="first">The first position still to send; earlier pages are on the site.</param>
        /// <param name="total">How many pages the book has.</param>
        /// <param name="send">Sends one page at its position.</param>
        /// <param name="started">Told a page is going out now, before the request is made.</param>
        /// <param name="accepted">Told the site took the page, after the answer arrives.</param>
        /// <param name="cancellationToken">Stops new pages; the ones in flight are awaited.</param>
        public static async Task<PageWindowResult> RunAsync(
            int first,
            int total,
            Func<int, Task<PageUpload>> send,
            Action<int>? started = null,
            Action<int>? accepted = null,
            CancellationToken cancellationToken = default)
        {
            if (send is null)
                throw new ArgumentNullException(nameof(send));

            var taken = new bool[Math.Max(0, total)];
            var running = new List<(int Position, Task<Outcome> Work)>(MaxInFlight);

            PageUpload? refusal = null;
            var failedPosition = -1;
            var cancelled = false;
            var next = Math.Max(0, first);

            while (true)
            {
                while (refusal is null && !cancelled && next < total && running.Count < MaxInFlight)
                {
                    // The token is read here as well as passed on: a page that has just been
                    // cancelled must not be followed by another one merely because the window
                    // still has room.
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }

                    var position = next++;
                    started?.Invoke(position);
                    running.Add((position, SendOneAsync(position, send, cancellationToken)));
                }

                if (running.Count == 0)
                    break;

                var finished = await Task
                    .WhenAny(running.Select(item => item.Work))
                    .ConfigureAwait(false);

                var index = running.FindIndex(item => ReferenceEquals(item.Work, finished));
                var outcome = await finished.ConfigureAwait(false);
                running.RemoveAt(index);

                if (outcome.Cancelled)
                {
                    cancelled = true;
                    continue;
                }

                if (outcome.Page is { IsAccepted: true })
                {
                    taken[outcome.Position] = true;
                    accepted?.Invoke(outcome.Position);
                    continue;
                }

                if (refusal is null)
                {
                    refusal = outcome.Page;
                    failedPosition = outcome.Position;
                }
            }

            return new PageWindowResult(taken, refusal, failedPosition, cancelled);
        }

        /// <summary>One send, with a cancellation turned into an outcome rather than a throw.</summary>
        private static async Task<Outcome> SendOneAsync(
            int position,
            Func<int, Task<PageUpload>> send,
            CancellationToken cancellationToken)
        {
            try
            {
                return new Outcome(position, await send(position).ConfigureAwait(false), false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new Outcome(position, null, true);
            }
        }

        /// <summary>What became of one page, in a shape the window can hold.</summary>
        private readonly record struct Outcome(int Position, PageUpload? Page, bool Cancelled);
    }
}
