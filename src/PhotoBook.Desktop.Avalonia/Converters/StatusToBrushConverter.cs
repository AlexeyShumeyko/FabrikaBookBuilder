using System;
using System.Globalization;
using Avalonia.Data.Converters;
using PhotoBook.Shell.Services;
using PhotoBook.Core;

namespace PhotoBook.Shell.Converters
{
    /// <summary>
    /// The status pill's three colours: background, border and text.
    /// </summary>
    /// <remarks>
    /// The role comes from the markup as a parameter, so one converter serves all three.
    /// A converter per colour would be three types saying the same table three times, and the
    /// table is the part that has to stay right.
    /// </remarks>
    public class StatusToBrushConverter : IValueConverter
    {
        public static readonly StatusToBrushConverter Instance = new();

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            var status = value is ProjectStatus s ? s : ProjectStatus.NotFilled;
            var role = (parameter as string)?.ToLowerInvariant() ?? "bg";

            var token = (status, role) switch
            {
                (ProjectStatus.SuccessfullyCompleted, "border") => "Emerald200",
                (ProjectStatus.SuccessfullyCompleted, "text") => "Emerald700",
                (ProjectStatus.SuccessfullyCompleted, _) => "Emerald50",

                (ProjectStatus.Ready, "border") => "Amber200",
                (ProjectStatus.Ready, "text") => "Amber700",
                (ProjectStatus.Ready, _) => "Amber50",

                (_, "border") => "BorderDefault",
                (_, "text") => "TextDefault",
                _ => "Sunken"
            };

            return ThemeResources.Brush(token);
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();
    }
}
