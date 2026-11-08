using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using PhotoBook.Presentation;

namespace PhotoBook.Shell.Presentation
{
    /// <summary>
    /// The Avalonia answer to <see cref="PerfPhase.InstallElementCounter"/>: walks the live
    /// visual tree of the main window and reports how much of it exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same walk as the WPF shell's counter, against Avalonia's tree. It is what turns
    /// "the screen hung for three seconds" into "it built twelve thousand elements", and it
    /// is the number that shows whether the books list is really virtualizing.
    /// </para>
    /// <para>
    /// The walk is iterative for the same reason: a fifty-book run is thousands of nodes,
    /// and a recursive walk would only be correct until the first big project arrived.
    /// </para>
    /// </remarks>
    internal static class AvaloniaElementCounter
    {
        public static PerfPhase.VisualSize Count()
        {
            var window = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow;
            if (window is null) return new PerfPhase.VisualSize(0, 0);

            int count = 0;
            int maxDepth = 0;
            var stack = new Stack<(Visual Node, int Depth)>();
            stack.Push((window, 0));

            while (stack.Count > 0)
            {
                var (node, depth) = stack.Pop();
                count++;
                if (depth > maxDepth) maxDepth = depth;

                foreach (var child in node.GetVisualChildren())
                    stack.Push((child, depth + 1));
            }

            return new PerfPhase.VisualSize(count, maxDepth);
        }
    }
}
