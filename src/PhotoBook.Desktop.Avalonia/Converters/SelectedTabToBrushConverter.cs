using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PhotoBook.Core;

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
                // The selected tab is a white card lifted off the grey strip; the rest show
                // the strip through. There is no third state and no hover colour here - the
                // hover is the platform's, on top of whichever of these two is underneath.
                "bg" => selected ? Solid("#FFFFFF") : Solid("Transparent"),

                // The hairline is what makes the chosen tab visible now that the grey
                // track behind all four is gone. A white pill on a white bar needs one
                // edge to say "this one".
                "border" => selected ? Solid("#E2E8F0") : Solid("Transparent"),

                "fg" => selected ? Solid("#4F46E5") : Solid("#475569"),

                _ => Solid("Transparent")
            };
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();

        private static IBrush Solid(string hex) => new SolidColorBrush(Color.Parse(hex));
    }
}
