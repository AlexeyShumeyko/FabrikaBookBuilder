using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoBook.Presentation
{
    /// <summary>
    /// Which shape the cards of a combined run should have, decided from the photographs
    /// that are actually in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule is a majority, not an average and not whichever photograph happened to be
    /// first. A run of ten narrow spreads must not be reshaped by one wide photograph
    /// dropped into it by mistake, and the cards must not swing back and forth while the
    /// photographer loads a folder one file at a time.
    /// </para>
    /// <para>
    /// Sizes that belong to the same print format differ by a few thousandths (2457x3602
    /// and 2460x3600 are both the same paper), so exact grouping would count them as
    /// different formats and no majority would ever form. They are grouped by closeness
    /// instead, and the winner is the group with the most photographs.
    /// </para>
    /// <para>
    /// A tie is the only ambiguous case, and it is settled without inventing a midpoint:
    /// the current shape keeps its place while it is still one of the tied groups, and when
    /// there is no current shape the first photograph decides. Averaging the two - the old
    /// median behaviour - produced a shape that belonged to neither format.
    /// </para>
    /// </remarks>
    public static class RunFrameAspectPolicy
    {
        /// <summary>The mockup's 16/10, used until a spread has been measured.</summary>
        public const double DefaultAspect = 1.6d;

        /// <summary>
        /// How far apart two measured ratios may be and still count as the same format.
        /// Ten percent covers the variation between scans of one paper size while keeping
        /// 0.68 and 1.9 decisively apart.
        /// </summary>
        private const double SameFormatTolerance = 0.10d;

        /// <summary>
        /// The shape for the run. <paramref name="aspects"/> is one ratio per photograph,
        /// in reading order; the cover must not be in it. <paramref name="current"/> is the
        /// shape already on screen, used only to break a tie.
        /// </summary>
        public static double Decide(IReadOnlyList<double> aspects, double? current)
        {
            if (aspects == null) throw new ArgumentNullException(nameof(aspects));

            var measured = aspects.Where(a => a > 0d).ToList();
            if (measured.Count == 0) return DefaultAspect;

            var groups = Group(measured);
            int largest = groups.Max(g => g.Count);
            var tied = groups.Where(g => g.Count == largest).ToList();

            List<double>? winner = null;
            if (current is double c && c > 0d)
                winner = tied.FirstOrDefault(g => IsSameFormat(MedianOfSorted(g), c));

            winner ??= tied.FirstOrDefault(g => g.Contains(measured[0]));
            winner ??= tied[0];

            return MedianOfSorted(winner);
        }

        private static List<List<double>> Group(List<double> values)
        {
            var groups = new List<List<double>>();

            // Sorted so a group is built from neighbouring values rather than from
            // whichever file happened to be read first. Append keeps each group sorted,
            // which is what lets the median be read without sorting it again on every
            // photograph - a run page announces its thumbnail, and this runs for each.
            foreach (var value in values.OrderBy(v => v))
            {
                var group = groups.FirstOrDefault(g => IsSameFormat(MedianOfSorted(g), value));
                if (group == null)
                    groups.Add(new List<double> { value });
                else
                    group.Add(value);
            }

            return groups;
        }

        private static bool IsSameFormat(double left, double right)
        {
            if (left <= 0d || right <= 0d) return false;
            if (left < right) (left, right) = (right, left);
            return (left - right) / left <= SameFormatTolerance;
        }

        /// <summary>The median of a list already in ascending order.</summary>
        private static double MedianOfSorted(List<double> values)
        {
            int mid = values.Count / 2;
            return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2d;
        }
    }
}
