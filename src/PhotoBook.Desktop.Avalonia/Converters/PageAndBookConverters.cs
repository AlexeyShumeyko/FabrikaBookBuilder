using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia;
using Avalonia.Media;
using PhotoBook.Core;

namespace PhotoBook.Shell.Converters
{
    /// <summary>
    /// What a slot says about itself: "Cover" or "Spread N", and in which colour.
    /// </summary>
    /// <remarks>
    /// One caption per card, four states behind it. A photographer reads a row of cards far
    /// faster than they read a legend, and the cover is the one that must be findable at a
    /// glance - so it is the only one with a colour of its own.
    /// </remarks>
    public class PageCaptionConverter : IValueConverter
    {
        public static readonly PageCaptionConverter Instance = new();

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            if (value is not Page page) return string.Empty;

            var role = (parameter as string)?.ToLowerInvariant() ?? "text";

            return role switch
            {
                "text" => page.IsCover ? "Обложка" : $"Разворот {page.DisplayIndex}",
                "brush" => page.IsCover ? Brush("#4F46E5") : Brush("#64748B"),

                // The cover is the one slot that has to be findable in a row of twenty, so it
                // is framed in the accent colour and two pixels. Everything else is a hairline.
                //
                // The frame says which position this is, not whether it is done. An empty
                // cover keeps the frame: it is still the cover, and a row of waiting slots
                // should have exactly one place in it that is meant for something specific.
                // It was tried without the accent on an empty cover, and the row read as
                // eleven interchangeable blanks with no beginning.
                "border" => page.IsCover ? Brush("#6366F1") : Brush("#E2E8F0"),

                // A Thickness, not a number. Bound to a plain double this conversion is not
                // made at all, the border comes out with no thickness, and every slot on the
                // screen loses its frame - which reads as "these cards have no border" rather
                // than as a failed conversion.
                "borderthickness" => page.IsCover ? new Thickness(2) : new Thickness(1),

                _ => string.Empty
            };
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();

        internal static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    }

    /// <summary>
    /// A book's structure state: complete or not, and what that looks like.
    /// </summary>
    /// <remarks>
    /// Both colours are saturated on purpose. An unvalidated structure is a fact the
    /// photographer needs before they export, not a hint - and a pale amber on a white card
    /// reads as decoration, which is what it would then be.
    /// </remarks>
    public class BookStatusConverter : IValueConverter
    {
        public static readonly BookStatusConverter Instance = new();

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            var valid = value is Book book && book.IsValid;

            return (parameter as string)?.ToLowerInvariant() switch
            {
                "text" => valid ? "Структура корректна" : "Структура неполная",
                "glyph" => valid ? "\uE73E" : "\uE7BA",   // the tick and the warning
                "brush" => valid ? PageCaptionConverter.Brush("#059669") : PageCaptionConverter.Brush("#D97706"),
                _ => string.Empty
            };
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();
    }
}
