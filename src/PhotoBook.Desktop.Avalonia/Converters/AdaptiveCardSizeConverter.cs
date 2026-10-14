using System.Collections.Generic;
using System;
using System.Globalization;
using Avalonia.Data.Converters;
using PhotoBook.Core;
using PhotoBook.Presentation;

namespace PhotoBook.Shell.Converters
{
    /// <summary>
    /// Card size for a spread card: the photo frame's width and height, adapted to how much
    /// room the book block actually has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Transcribed from the WPF converter constant for constant, because every number here
    /// was measured against a complaint: "the row does not fill" was reported twice, and each
    /// time the cause was a limit somebody had added for taste.
    /// </para>
    /// <para>
    /// Natural size is 190 tall and that times the book's own format wide. The height is the
    /// mockup's value on purpose - the card's shape is the one thing a photographer cannot
    /// work with, so the format decides the width and never the height.
    /// </para>
    /// <para>
    /// The decision uses ONLY the available width, never the number of items, so a half-empty
    /// last row cannot change the size. There is no cap on how far a card may shrink; the
    /// floor is 130 and that is set by what the card has to say, not by taste.
    /// </para>
    /// </remarks>
    public class AdaptiveCardSizeConverter : IMultiValueConverter
    {
        /// <summary>Mockup card height. See doc/DESIGN_SPEC.md 5.2.</summary>
        private const double DefaultNaturalHeight = 190d;

        /// <summary>Gap between cards. Must match the card's margin in the view.</summary>
        private const double CardMargin = 16d;

        /// <summary>
        /// What the card adds around the photo frame: a one-pixel border on each side. The
        /// wrap panel packs the CARD, not the frame, and leaving this out costs exactly the
        /// last card of the row.
        /// </summary>
        private const double CardChrome = 2d;

        /// <summary>
        /// How much dead space may be left on the right before the cards are scaled down to
        /// win one more per row. A flat few pixels, not a share of a step.
        /// </summary>
        private const double MaxLeftover = 12d;

        /// <summary>Floor set by what the card has to say, not by how it looks.</summary>
        private const double MinWidth = 130d;

        /// <summary>Upper clamp, so an extreme panorama cannot become a banner.</summary>
        private const double MaxWidth = 480d;

        /// <summary>The mockup's 16/10, used until a book's own format is known.</summary>
        private const double FallbackAspect = 1.6d;

        public object? Convert(
            IList<object?> values,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            double available = values is { Count: > 0 } ? ToDouble(values[0]) : 0d;
            double aspect = values is { Count: > 1 }
                            && values[1] is Page page
                            && page.FrameAspect > 0d
                ? page.FrameAspect
                : FallbackAspect;

            bool wantHeight = false;
            double naturalHeight = DefaultNaturalHeight;

            var parts = (parameter as string)?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        ?? Array.Empty<string>();
            if (parts.Length > 0)
                wantHeight = string.Equals(parts[0].Trim(), "height", StringComparison.OrdinalIgnoreCase);
            if (parts.Length > 1 &&
                double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double h) &&
                h > 0d)
                naturalHeight = h;

            PerfPhase.Count("fit w=" + (int)Math.Round(available) + " h=" + (int)Math.Round(naturalHeight));

            double naturalWidth = naturalHeight * aspect;
            double width = FitWidth(available, naturalWidth);

            return Math.Round(wantHeight ? width / aspect : width, 1);
        }

        /// <summary>
        /// The width to lay out with: the design size, scaled down by the factor that fits one
        /// more card per row when such a factor exists without breaking anything.
        /// </summary>
        private static double FitWidth(double available, double naturalWidth)
        {
            double natural = Math.Clamp(naturalWidth, MinWidth, MaxWidth);

            // First layout pass: nothing is measured yet, so start from the design size.
            if (available <= 0d) return natural;

            // A window narrower than one card: fill the width rather than overflow it.
            if (natural + CardChrome + CardMargin > available)
                return Math.Max(available - CardChrome - CardMargin, MinWidth);

            // The wrap panel wraps as soon as the NEXT item's full step no longer fits, and
            // that step is the whole CARD: frame plus border on both sides plus margin.
            double step = natural + CardChrome + CardMargin;
            int perRow = (int)(available / step);
            if (perRow < 1) perRow = 1;

            double leftover = available - perRow * step;
            if (leftover <= MaxLeftover) return natural;   // already flush, leave it alone

            // Frame width that would fit one more card per row, with a pixel of slack so
            // floating point rounding cannot push the last item over the edge.
            double target = available / (perRow + 1) - CardChrome - CardMargin - 1d;

            if (target >= MinWidth && target < natural)
                return target;

            return natural;
        }

        private static double ToDouble(object? o) => o switch
        {
            double d => d,
            int i => i,
            float f => f,
            _ => 0d
        };

        public object?[] ConvertBack(
            object? value,
            Type[] targetTypes,
            object? parameter,
            CultureInfo culture)
            => Array.Empty<object?>();
    }
}
