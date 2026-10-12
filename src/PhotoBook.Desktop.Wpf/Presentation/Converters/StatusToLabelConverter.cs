using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PhotoBook.Core;
using PhotoBookRenamer.Presentation.ViewModels;

namespace PhotoBookRenamer.Presentation.Converters
{
    /// <summary>
    /// ProjectStatus -> the coloured dot next to the pill label.
    /// </summary>
    public class StatusToLabelConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var status = value is ProjectStatus s ? s : ProjectStatus.NotFilled;
            // "dot" returns the dot brush, anything else returns the Russian label.
            if (string.Equals(parameter as string, "dot", StringComparison.OrdinalIgnoreCase))
            {
                var hex = status switch
                {
                    ProjectStatus.SuccessfullyCompleted => "#10B981", // emerald-500
                    ProjectStatus.Ready => "#F59E0B",                 // amber-500
                    _ => "#94A3B8"                                    // slate-400
                };
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
            }

            return status switch
            {
                ProjectStatus.SuccessfullyCompleted => "Готов к печати",
                ProjectStatus.Ready => "В процессе сборки",
                _ => "Черновик"
            };
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
