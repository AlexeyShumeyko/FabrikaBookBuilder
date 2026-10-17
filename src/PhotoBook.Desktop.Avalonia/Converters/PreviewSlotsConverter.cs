using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhotoBook.Shell.Converters
{
    /// <summary>
    /// A card's three picture slots, with the empty ones kept in place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The strip used to disappear when a project had no photographs in it. That made the
    /// card shorter, and because a row of cards is only as tall as its tallest card, it made
    /// the whole grid depend on what happened to be in the projects: an empty card was short
    /// on its own, and exactly the same card became a different size the moment it moved
    /// next to one with photographs in it.
    /// </para>
    /// <para>
    /// So the strip is three slots always. A slot with a photograph shows it; a slot without
    /// one shows an empty tile, which says what is true - this run has no photographs here
    /// yet - instead of making the card shorter and the layout jump.
    /// </para>
    /// </remarks>
    public class PreviewSlotsConverter : IValueConverter
    {
        /// <summary>How many tiles a card has. Fixed, and the reason cards are one size.</summary>
        public const int Slots = 3;

        public static readonly PreviewSlotsConverter Instance = new();

        /// <summary>One tile: a photograph, or the absence of one.</summary>
        public sealed record Slot(string? Path, bool IsEmpty);

        private static IReadOnlyList<Slot> Pad(ObservableCollection<string>? photos)
        {
            var slots = new Slot[Slots];
            var count = photos?.Count ?? 0;

            for (var i = 0; i < Slots; i++)
            {
                slots[i] = i < count && !string.IsNullOrEmpty(photos![i])
                    ? new Slot(photos[i], false)
                    : new Slot(null, true);
            }

            return slots;
        }

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => value is ObservableCollection<string> photos ? Pad(photos) : Pad(null);

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();
    }
}
