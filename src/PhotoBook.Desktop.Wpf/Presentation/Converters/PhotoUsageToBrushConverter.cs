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
    /// The fill of a photo's state badge in the file list: ONE saturated colour per state,
    /// white text on top, no border. That is the whole design.
    ///
    /// The we asked for it four times and the fourth answer is this one, in his words:
    /// "like you paint a button". The three that came before it are the reason the roles are
    /// gone rather than extended:
    ///
    ///   1. a tint over the whole row - "I asked for the status, not the cell";
    ///   2. a coloured frame only - "barely visible";
    ///   3. a `-100` tint with dark text and a frame - "there is NO fill", which is what a
    ///      pale tint on a 10px label looks like: white with a hint, so all three states
    ///      read as grey-with-a-border and none of them reads as its own colour.
    ///
    /// So there is exactly one brush, it is saturated, and the label is white on it. A
    /// converter with a "border" and a "text" role is what let attempt 2 and attempt 3 exist
    /// side by side and disagree with attempt 1; there is nothing left here to disagree.
    /// Free is slate because it needs no attention, assigned is the green of "done", and
    /// shared is the brand colour - the one state the photographer has to notice, since the
    /// same picture is going into every book.
    /// </summary>
    public class PhotoUsageToBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // `{Binding Usage, Converter=...}` hands over the VALUE of Usage - the enum -
            // not the PhotoFileInfo it came from. An `is PhotoFileInfo` test therefore never
            // matched, every state fell through to "free", and all three badges were the
            // same grey while their labels said "Общий", "Назначен" and "Свободен". The
            // we saw that as "it is outlined and filled with grey" and was right: the
            // colour was grey, always. Both shapes are accepted so the next binding style
            // cannot reintroduce it.
            var usage = value switch
            {
                PhotoUsage u => u,
                PhotoFileInfo file => file.Usage,
                _ => PhotoUsage.Free
            };

            return usage switch
            {
                PhotoUsage.Assigned => Brush(0x10, 0xB9, 0x81),   // emerald-500
                PhotoUsage.Shared => Brush(0x63, 0x66, 0xF1),   // brand-500
                _ => Brush(0x94, 0xA3, 0xB8)    // slate-400
            };
        }

        private static System.Windows.Media.SolidColorBrush Brush(byte r, byte g, byte b)
            => new(System.Windows.Media.Color.FromRgb(r, g, b));

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
