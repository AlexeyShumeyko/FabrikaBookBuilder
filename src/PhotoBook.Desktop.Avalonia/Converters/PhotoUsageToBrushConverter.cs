using System;
using System.Globalization;
using Avalonia.Data.Converters;
using PhotoBook.Shell.Services;
using PhotoBook.Core;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Shell.Converters
{
    /// <summary>
    /// The file list's state badge: a quiet chip with a word, a wash and a hairline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each state owns one family of three colours - ink, fill and ring - and the caller asks
    /// for the part it needs. Shared is the product's own gold, because it is the one state
    /// that is the same in every book; assigned is the teal of a photograph that is in use;
    /// free is the grey of one that still waits.
    /// </para>
    /// <para>
    /// Filled like a button the badge was an orange slab among cream controls in daylight.
    /// On paper a state is a word in gold on a gold wash, which is what the prototype draws,
    /// and at night the same chip is cream on a thin wash of the gold.
    /// </para>
    /// </remarks>
    public class PhotoUsageToBrushConverter : IValueConverter
    {
        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => ThemeResources.Brush(Token(value, parameter));

        /// <summary>
        /// The token a state is painted with, for the part of the chip the caller asked for.
        /// </summary>
        /// <remarks>
        /// Ink, fill and ring come from the same family, so a chip cannot end up carrying the
        /// word of one state on the wash of another. An unknown part answers with the fill,
        /// which is what the badge was before the ink and the ring were asked for by name.
        /// </remarks>
        private static string Token(object? value, object? parameter)
        {
            var family = value switch
            {
                PhotoUsage.Shared => "StatusShared",
                PhotoUsage.Assigned => "StatusTeal",
                _ => "StatusMuted"
            };

            var part = (parameter as string)?.ToLowerInvariant() switch
            {
                "ink" => "Ink",
                "border" => "Border",
                _ => "Fill"
            };

            return family + part;
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();

    }
}
