using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PhotoBook.Core;

namespace PhotoBook.Shell.Converters
{
    /// <summary>
    /// The status pill's text and its dot.
    /// </summary>
    /// <remarks>
    /// Three statuses, three colours, taken by role rather than by index so that adding a
    /// fourth status cannot silently reuse the wrong green. The values are transcribed from
    /// the design spec, not from the WPF converter, so the two can be compared.
    /// </remarks>
    public class StatusToLabelConverter : IValueConverter
    {
        public static readonly StatusToLabelConverter Instance = new();

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            var status = value is ProjectStatus s ? s : ProjectStatus.NotFilled;

            if (Is(parameter, "dot"))
            {
                return Brush(status switch
                {
                    ProjectStatus.SuccessfullyCompleted => "#10B981", // emerald-500
                    ProjectStatus.Ready => "#F59E0B",                // amber-500
                    _ => "#94A3B8"                                     // slate-400
                });
            }

            return status switch
            {
                ProjectStatus.SuccessfullyCompleted => "Готов к печати",
                ProjectStatus.Ready => "В процессе сборки",
                _ => "Черновик"
            };
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();

        internal static bool Is(object? parameter, string role)
            => string.Equals(parameter as string, role, StringComparison.OrdinalIgnoreCase);

        internal static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    }
}