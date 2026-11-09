using System;
using System.Globalization;
using Avalonia.Data.Converters;
using PhotoBook.Core;

namespace PhotoBook.Shell.Converters
{
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
                _ => null
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
