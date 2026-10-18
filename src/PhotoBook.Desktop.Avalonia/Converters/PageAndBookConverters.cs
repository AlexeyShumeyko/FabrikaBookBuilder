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
            // The role is read first and the page second, and that order is the whole of a
            // crash that was here: asked for a colour while the value was not a page at
            // all, this used to answer with an empty string, and a colour is not an empty
            // string - it is a colour, or nothing at all, and an empty string is neither.
            //
            // Rebuilding a run empties its list of slots and refills it, and a slot whose
            // binding is re-read in that moment is looking at nothing. So "nothing" is a
            // value this has to have an answer for.
            var role = (parameter as string)?.ToLowerInvariant() ?? "text";
            var page = value as Page;
            var isCover = page?.IsCover ?? false;

            return role switch
            {
                "text" => page is null ? string.Empty : page.IsCover ? "Обложка" : $"Разворот {page.DisplayIndex}",
                "brush" => isCover ? Brush("#4F46E5") : Brush("#64748B"),

                // The cover is the one slot that has to be findable in a row of twenty, so it
                // is framed in the accent colour and two pixels. Everything else is a hairline.
                //
                // The frame says which position this is, not whether it is done. An empty
                // cover keeps the frame: it is still the cover, and a row of waiting slots
                // should have exactly one place in it that is meant for something specific.
                // It was tried without the accent on an empty cover, and the row read as
                // eleven interchangeable blanks with no beginning.
                "border" => isCover ? Brush("#6366F1") : Brush("#E2E8F0"),

                // A Thickness, not a number. Bound to a plain double this conversion is not
                // made at all, the border comes out with no thickness, and every slot on the
                // screen loses its frame - which reads as "these cards have no border" rather
                // than as a failed conversion.
                "borderthickness" => isCover ? new Thickness(2) : new Thickness(1),

                // What the action says while the pointer is on the card. Two of them, and
                // they are not two labels for one thing: a spread can be promoted to be the
                // cover, and the cover cannot be promoted to be the cover - it already is
                // one. What it can be given is a different photograph, so that is what it
                // offers, and the released program does exactly this.
                "actiontext" => page is null
                    ? string.Empty
                    : page.IsCover ? "Заменить" : "Назначить обложкой",

                // Violet for the cover's own action and green for the promotion, as in the
                // released program: the colour says which of the two things this is.
                "actionbrush" => isCover ? Brush("#4F46E5") : Brush("#059669"),

                "actionname" => page is null
                    ? string.Empty
                    : page.IsCover ? "Заменить обложку" : "Назначить обложкой",

                // Null rather than an empty string: a role nobody asked for should leave the
                // property alone, and an empty string would be read as a value of some kind.
                _ => null
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
