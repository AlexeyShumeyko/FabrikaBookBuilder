using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PhotoBook.Shell.Converters
{
    /// <summary>
    /// The frame of a run field: the ordinary hairline while the box holds a number, red while it
    /// does not.
    /// </summary>
    /// <remarks>
    /// A frame and not a message. The released program answered an unfinished number field the
    /// same way, and the port had inherited the engine's default instead: the conversion failure
    /// was printed under the box in its own technical words, and the panel grew to fit a sentence
    /// nobody asked for. A sentence about parsing belongs in the journal; on screen the answer is
    /// "this box needs a number", which is what a red frame says.
    /// </remarks>
    public class RunFieldFrameConverter : IValueConverter
    {
        public static readonly RunFieldFrameConverter Instance = new();

        private static readonly IBrush Valid = Brushes.Transparent;
        private static readonly IBrush Invalid = new SolidColorBrush(Color.Parse("#F87171"));

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => value is false ? Invalid : Valid;

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();
    }
}
