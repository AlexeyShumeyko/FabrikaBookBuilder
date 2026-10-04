using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PhotoBookRenamer.Presentation
{
    /// <summary>
    /// Times the phases of opening a project, so "the window hangs for a second and a half"
    /// can be turned into a number and a name.
    ///
    /// It exists because every guess about this cost was wrong: removing the per-slot
    /// rounded clip (290 clips) bought 8%, and virtualizing the books list bought nothing
    /// at all, because the list never actually virtualized. Guessing cost a day; a stopwatch
    /// costs a line.
    ///
    /// Off unless <c>FBR_PERF_TRACE=1</c>. When off, <see cref="Mark"/> is a static call on
    /// an empty list - no allocation, no string formatting, no file handle - so the open
    /// path pays nothing for having it. The log is flushed once, at <see cref="Write"/>,
    /// because a per-phase file open would be exactly the kind of IO this is measuring.
    /// </summary>
    internal static class PerfPhase
    {
        private static readonly bool Enabled =
            Environment.GetEnvironmentVariable("FBR_PERF_TRACE") == "1";

        private static readonly List<(string Name, long Ms)> Marks = new();
        private static readonly Stopwatch Clock = new();
        private static long _last;

        public static bool IsEnabled => Enabled;

        /// <summary>Forgets the previous run. Cheap and idempotent.</summary>
        public static void Reset()
        {
            if (!Enabled) return;
            Marks.Clear();
            Clock.Restart();
            _last = 0;
        }

        /// <summary>Records a phase boundary. The value is the time since the previous one.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Counts = new();

        /// <summary>
        /// Counts a call. Binding converters run during layout, so a count that is orders of
        /// magnitude larger than the number of elements is a layout that keeps re-running
        /// itself - which is far more expensive than the elements ever are.
        /// </summary>
        public static void Count(string what)
        {
            if (!Enabled) return;
            Counts.AddOrUpdate(what, 1, (_, n) => n + 1);
        }

        public static string CountsAsText()
        {
            var sb = new StringBuilder();
            foreach (var kv in Counts) sb.AppendLine($"{kv.Value,7} x {kv.Key}");
            return sb.ToString();
        }
        public static void Mark(string name)
        {
            if (!Enabled) return;
            long now = Clock.ElapsedMilliseconds;
            Marks.Add((name, now - _last));
            _last = now;
        }

        /// <summary>
        /// Writes the log. Called at the end of the operation, not per phase.
        /// </summary>
        /// <summary>
        /// Counts the elements the view has actually built. The number that decides whether
        /// virtualization works: 290 slot cards is ~6,500 elements, two or three books ~1,000.
        /// </summary>
        public static void CountElements(string label)
        {
            if (!Enabled) return;

            try
            {
                var window = System.Windows.Application.Current?.MainWindow;
                if (window == null) return;

                int count = 0;
                int maxDepth = 0;
                var stack = new Stack<(System.Windows.DependencyObject Node, int Depth)>();
                stack.Push((window, 0));
                while (stack.Count > 0)
                {
                    var (node, depth) = stack.Pop();
                    count++;
                    if (depth > maxDepth) maxDepth = depth;

                    int children = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
                    for (int i = 0; i < children; i++)
                        stack.Push((System.Windows.Media.VisualTreeHelper.GetChild(node, i), depth + 1));
                }

                Mark($"{label}: {count} elements, depth {maxDepth}");
            }
            catch (Exception ex)
            {
                Mark($"{label}: element count failed ({ex.GetType().Name})");
            }
        }

        public static void Write(string operation)
        {
            if (!Enabled) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"== {operation} == total {Clock.ElapsedMilliseconds} ms");
                foreach (var (name, ms) in Marks) sb.AppendLine($"{ms,6} ms  {name}");

                sb.AppendLine("  calls during the operation:");
                sb.Append(CountsAsText());

                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhotoBookRenamer");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "perf.log"), sb.ToString());
            }
            catch
            {
                // A diagnostic that throws is worse than no diagnostic.
            }
        }
    }
}
