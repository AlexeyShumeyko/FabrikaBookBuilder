using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace PhotoBook.Shell.Converters
{
    /// <summary>
    /// The width of one card in a three-across row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The row width is MEASURED, not derived. Subtracting a hand-picked reserve for margins
    /// is how the WPF shell once sized cards 10 pixels short of the edge - and 10 pixels is
    /// a whole card, so the row fitted two where it had planned three and left a fifth of the
    /// window empty. This reads the row's own width and gives back exactly the two gaps.
    /// </para>
    /// <para>
    /// It is a converter rather than a panel because the layout has to be a WrapPanel: a
    /// uniform grid gives every cell the height of the tallest one, which clips a short card's
    /// statistics and hides its footer. A wrap panel sizes each row to its own content, and
    /// needs the item width to be told - which is what this is for.
    /// </para>
    /// </remarks>
    public class CardWidthConverter : IValueConverter
    {
        /// <summary>One gap between cards, and one after the last, per the spec's 24.</summary>
        private const double Gap = 24;

        /// <summary>Columns. Three is what the spec asks for above the large breakpoint.</summary>
        private const int Columns = 3;

        public static readonly CardWidthConverter Instance = new();

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            // The value is the row's own width, handed over as a double by the binding.
            if (value is not double width || width <= 0)
            {
                // Before the first layout pass there is no width to divide. Returning a
                // plausible number keeps the first frame from collapsing to nothing and
                // correcting itself on the next pass.
                return 340d;
            }

            var usable = width - (Gap * Columns);
            if (usable <= 0) return width;

            return Math.Floor(usable / Columns);
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();
    }
}
