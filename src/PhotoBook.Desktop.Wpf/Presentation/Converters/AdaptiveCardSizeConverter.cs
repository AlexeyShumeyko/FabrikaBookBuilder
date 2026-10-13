using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PhotoBook.Core;
using PhotoBookRenamer.Presentation.ViewModels;

namespace PhotoBookRenamer.Presentation.Converters
{
    /// <summary>
    /// Card size for a spread card: the photo frame's width and height, adapted to how
    /// much room the book block actually has.
    ///
    /// Natural size: <see cref="DefaultNaturalHeight"/> tall, and that times the book's
    /// own format wide (<see cref="Page.FrameAspect"/>). The height is the mockup's value
    /// on purpose - the card's shape is the one thing a photographer cannot work with, so
    /// the format decides the width and never the height.
    ///
    /// Adaptation: if a whole extra card does not fit in the row but the leftover space is
    /// worth spending, the card is scaled by exactly the factor that makes it fit, both
    /// sides together, so the format and the card's proportions are preserved. The decision
    /// uses ONLY the available width, never the number of items, so a half-empty last row
    /// cannot change the size.
    ///
    /// The row is filled, always. There is no cap on how far a card may shrink: the only
    /// limit is <see cref="MinWidth"/>. A leftover bigger than <see cref="MaxLeftover"/> is
    /// a hole in the layout that the photographer reads as a bug - we reported
    /// exactly that, twice: two cards in a row with a fifth of the width empty beside them.
    /// The proportional cap that used to be here (20%) is what produced it.
    ///
    /// Usage: MultiBinding of the row area's width and the Page, with
    /// ConverterParameter="width" or "height". A second comma-separated number overrides
    /// <see cref="DefaultNaturalHeight"/> for that binding, i.e. "width,150".
    ///
    /// The override exists because the two modes do not have the same room. The
    /// unique-folder mode gives its cards the whole window, and 190px is the size the
    /// mockup was drawn at. The combined mode shares the window with a 320px file list,
    /// so its design height is smaller.
    ///
    /// The width must be EXACT, and it must be something the cards cannot influence. The
    /// row area is the slots' own <c>ItemsControl</c> - a vertical stack under a
    /// <c>ScrollViewer</c> hands its children a fixed width, so that width never depends on
    /// how wide the cards came out, and the scrollbar is already subtracted from it.
    /// Deriving it instead (ScrollViewer's own width minus a hand-picked reserve) was
    /// wrong by 10px, and 10px is a whole card: the rule sized the cards for three per row
    /// and the row then fitted two, leaving a fifth of the width empty.
    /// </summary>
    public class AdaptiveCardSizeConverter : IMultiValueConverter
    {
        /// <summary>Mockup card height. See doc/DESIGN_SPEC.md 5.2.</summary>
        protected const double DefaultNaturalHeight = 190d;

        /// <summary>Gap between cards. Must match SpreadCard's Margin in the view.</summary>
        private const double CardMargin = 16d;

        /// <summary>
        /// What the card adds around the photo frame: a 1px border on each side. The
        /// WrapPanel packs the CARD, not the frame, and leaving this out costs exactly the
        /// last card of the row.
        /// </summary>
        private const double CardChrome = 2d;

        /// <summary>
        /// How much dead space may be left on the right before the cards are scaled down
        /// to win one more per row. A flat few pixels, not a share of a step: a fifth of a
        /// step is a fifth of the row, and a hole that size is exactly what we
        /// reported as "the row does not fill". The design size is kept only when the row
        /// is already flush.
        /// </summary>
        private const double MaxLeftover = 12d;

        /// <summary>
        /// Floor set by what the card has to SAY, not by taste: at rest the footer is one
        /// line - "Разворот 1 (общий)" plus the bin - and on hover that label is REPLACED
        /// by the "Во все книги" word rather than pushed aside, so 18px of padding, 22px of
        /// bin and ~80px of word is the whole budget: 130px holds it. A very portrait
        /// format - 0.67 post-prints - is 100px wide at the design height, so such a book
        /// gets a LARGER frame rather than a clipped caption. The frame keeps the book's
        /// aspect either way; only its size changes.
        /// </summary>
        private const double MinWidth = 130d;

        /// <summary>Upper clamp, so an extreme panorama cannot become a banner.</summary>
        private const double MaxWidth = 480d;

        private const double FallbackAspect = 1.6d;   // the mockup's 16/10

        public object Convert(object?[]? values, Type targetType, object? parameter, CultureInfo culture)
        {
            double available = values != null && values.Length > 0 ? ToDouble(values[0]) : 0d;
            double aspect = values != null && values.Length > 1 && values[1] is Page page && page.FrameAspect > 0d
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

            PhotoBookRenamer.Presentation.PerfPhase.Count("fit w=" + (int)Math.Round(available) + " h=" + (int)Math.Round(naturalHeight));
            double naturalWidth = naturalHeight * aspect;
            double width = FitWidth(available, naturalWidth);

            return Math.Round(wantHeight ? width / aspect : width, 1);
        }

        /// <summary>
        /// The width to lay out with. <paramref name="naturalWidth"/> is the designed size;
        /// the answer is that size scaled down by the factor that fits one more card per
        /// row, when such a factor exists without breaking anything.
        /// </summary>
        private static double FitWidth(double available, double naturalWidth)
        {
            double natural = Math.Clamp(naturalWidth, MinWidth, MaxWidth);

            // First layout pass: nothing is measured yet, so start from the design size.
            if (available <= 0d) return natural;

            // A window narrower than one card: fill the width rather than overflow it.
            if (natural + CardChrome + CardMargin > available)
                return Math.Max(available - CardChrome - CardMargin, MinWidth);

            // WrapPanel wraps as soon as the NEXT item's full step no longer fits, and that
            // step is the whole CARD: frame + border on both sides + right margin. N cards
            // need N * step <= available. Counting the step without the card's border is
            // what left a card short of the edge.
            double step = natural + CardChrome + CardMargin;
            int perRow = (int)(available / step);
            if (perRow < 1) perRow = 1;

            double leftover = available - perRow * step;
            if (leftover <= MaxLeftover) return natural;   // already flush, leave it alone

            // Frame width that would fit one more card per row, with a pixel of slack so
            // floating point rounding cannot push the last item over the edge.
            double target = available / (perRow + 1) - CardChrome - CardMargin - 1d;

            // The floor is the ONLY limit: below it the caption stops being readable, and
            // above it the row is filled whatever that costs. A second, proportional limit
            // used to sit here and it is what left two cards in a row with a fifth of the
            // width empty - the exact hole we reported.
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

        public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
            => Array.Empty<object?>();
    }
}
