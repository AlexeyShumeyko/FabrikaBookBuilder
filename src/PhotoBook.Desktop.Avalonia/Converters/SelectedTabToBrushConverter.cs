using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PhotoBook.Core;
using PhotoBook.Shell.Services;

namespace PhotoBook.Shell.Converters
{
    /// <summary>
    /// Whether the tab in question is the current screen, as a colour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It exists because the obvious way - a style on the checked pseudo-class - does not
    /// survive the platform theme here. The theme's own rule for a checked toggle button is
    /// applied after ours and wins, so a selected tab came out in the platform's colour: a
    /// dark grey that belongs to no screen of this design. Proved rather than assumed - with
    /// the checked rule set to red, the selected tab stayed dark.
    /// </para>
    /// <para>
    /// Binding the colour to the state sidesteps the question of whose rule wins, because
    /// there is no rule to win: the tab asks "am I current?" and paints what it is told.
    /// </para>
    /// </remarks>
    public class SelectedTabToBrushConverter : IValueConverter
    {
        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            bool selected = value is true;

            return (parameter as string)?.ToLowerInvariant() switch
            {
                // The fill and the hairline around the chosen tab are not here: a cream card
                // is a gradient, and a gradient is painted by the styles from the theme. This
                // answers with colours only - what the words and the mark are made of.
                "fg" => selected ? ThemeResources.Brush("TextStrong") : ThemeResources.Brush("TextMuted"),

                // The mark beside the words goes gold on the chosen tab and stays muted on
                // the rest: the prototype lights the mark of the current step only, and four
                // gold marks in one tray read as four current steps.
                "icon" => selected ? ThemeResources.Brush("Accent") : ThemeResources.Brush("TextMuted"),

                _ => Brushes.Transparent
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
