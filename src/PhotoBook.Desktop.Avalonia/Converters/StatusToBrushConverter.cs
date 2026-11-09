using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
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

            var hex = (status, role) switch
            {
                (ProjectStatus.SuccessfullyCompleted, "border") => "#A7F3D0", // emerald-200
                (ProjectStatus.SuccessfullyCompleted, "text") => "#047857",    // emerald-700
                (ProjectStatus.SuccessfullyCompleted, _) => "#ECFDF5",          // emerald-50

                (ProjectStatus.Ready, "border") => "#FDE68A",                   // amber-200
                (ProjectStatus.Ready, "text") => "#B45309",                     // amber-700
                (ProjectStatus.Ready, _) => "#FFFBEB",                          // amber-50

                (_, "border") => "#E2E8F0",                                    // slate-200
                (_, "text") => "#334155",                                      // slate-700
                _ => "#F1F5F9"                                                 // slate-100
            };

            return new SolidColorBrush(Color.Parse(hex));
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();
    }
}
