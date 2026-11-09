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

            // The words live in one place now - ProjectStatusText - because this badge and
            // the status filter in the list are the same sentence about the same project, and
            // when they were written out separately they disagreed: the filter labelled
            // "готов к печати" selected the runs whose cards said "в процессе сборки", and
            // a person could not tell whether pressing it had done anything.
            return ProjectStatusText.Of(status);
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
