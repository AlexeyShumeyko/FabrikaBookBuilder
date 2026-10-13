using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using PhotoBook.Presentation;

namespace PhotoBookRenamer.Presentation
{
    /// <summary>
    /// The WPF answer to <see cref="PerfPhase.InstallElementCounter"/>: walks the live
    /// visual tree of the main window and reports how much of it exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole reason PerfPhase had to stop being a WPF class. Counting elements
    /// is a question only the engine that built the screen can answer, so the walk stays
    /// here and the timings stay with it.
    /// </para>
    /// <para>
    /// The walk is iterative on purpose. The tree is thousands of nodes deep-first, and the
    /// recursive version that lived inside PerfPhase was correct only because the stack was
    /// small enough - which is exactly the kind of assumption that survives until a big
    /// project arrives.
    /// </para>
    /// </remarks>
    internal static class WpfElementCounter
    {
        public static PerfPhase.VisualSize Count()
        {
            var window = Application.Current?.MainWindow;
            if (window == null) return new PerfPhase.VisualSize(0, 0);

            int count = 0;
            int maxDepth = 0;
            var stack = new Stack<(DependencyObject Node, int Depth)>();
            stack.Push((window, 0));

            while (stack.Count > 0)
            {
                var (node, depth) = stack.Pop();
                count++;
                if (depth > maxDepth) maxDepth = depth;

                int children = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < children; i++)
                    stack.Push((VisualTreeHelper.GetChild(node, i), depth + 1));
            }

            return new PerfPhase.VisualSize(count, maxDepth);
        }
    }
}
