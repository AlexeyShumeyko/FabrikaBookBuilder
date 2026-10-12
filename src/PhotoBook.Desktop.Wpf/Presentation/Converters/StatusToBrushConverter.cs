using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PhotoBook.Core;
using PhotoBookRenamer.Presentation.ViewModels;

namespace PhotoBookRenamer.Presentation.Converters
{
    ///
    /// The mockup draws the chip as <c>bg-{tone}-50 text-{tone}-700 border-{tone}-200/60</c>,
    /// which is three separate values, so the role is selected with ConverterParameter:
    /// "bg" (default), "border" or "text".
    /// </summary>
    public class StatusToBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var status = value is ProjectStatus s ? s : ProjectStatus.NotFilled;
            var role = (parameter as string)?.ToLowerInvariant() ?? "bg";

            string hex = (status, role) switch
            {
                (ProjectStatus.SuccessfullyCompleted, "border") => "#A7F3D0", // emerald-200
                (ProjectStatus.SuccessfullyCompleted, "text") => "#047857", // emerald-700
                (ProjectStatus.SuccessfullyCompleted, _) => "#ECFDF5", // emerald-50

                (ProjectStatus.Ready, "border") => "#FDE68A",                 // amber-200
                (ProjectStatus.Ready, "text") => "#B45309",                 // amber-700
                (ProjectStatus.Ready, _) => "#FFFBEB",                 // amber-50

                (_, "border") => "#E2E8F0",                                   // slate-200
                (_, "text") => "#334155",                                   // slate-700
                _ => "#F1F5F9"                                    // slate-100
            };

            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
