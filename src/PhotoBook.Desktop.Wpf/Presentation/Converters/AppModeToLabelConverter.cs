using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PhotoBook.Core;
using PhotoBookRenamer.Presentation.ViewModels;

namespace PhotoBookRenamer.Presentation.Converters
{
    /// AppMode -&gt; the label a saved project's card shows, so the user can see which
    /// mode it was created in before opening it.
    ///
    /// ConverterParameter picks the shape: "icon" returns the matching Segoe MDL2
    /// codepoint (the same glyph the tab uses, see doc/ICONS.md), "visibility" returns
    /// Visible only for the two real project modes - a project record saved before a mode
    /// was stamped still carries AppMode.StartScreen, and an empty chip would read as a
    /// bug rather than as missing data.
    /// </summary>
    public class AppModeToLabelConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not AppMode mode) return string.Empty;

            string? role = parameter as string;

            if (string.Equals(role, "visibility", StringComparison.OrdinalIgnoreCase))
                return mode is AppMode.UniqueFolders or AppMode.Combined
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            bool wantIcon = string.Equals(role, "icon", StringComparison.OrdinalIgnoreCase);

            // The two modes each own a colour, used everywhere a mode is named: the chip
            // on a project card, the icon on the mode-select cards, the empty states.
            // Sky for the folders mode, the app's indigo for the combined one. The folders
            // mode was emerald first and we rejected it - emerald is "Готов к печати"
            // and the "Структура корректна" tick - and grey second, for the same reason:
            // a mode chip has to stand out, and grey is what every status already is.
            if (role is not null && role.StartsWith("tint", StringComparison.OrdinalIgnoreCase))
            {
                bool folders = mode == AppMode.UniqueFolders;
                return role.ToLowerInvariant() switch
                {
                    "tintbg" => Brush(folders ? "#F0F9FF" : "#EEF2FF"),
                    "tintborder" => Brush(folders ? "#BAE6FD" : "#C7D2FE"),
                    "tinttext" => Brush(folders ? "#0369A1" : "#4338CA"),
                    "tinticon" => Brush(folders ? "#0284C7" : "#4F46E5"),
                    _ => Brushes.Transparent
                };
            }
            return (mode, wantIcon) switch
            {
                (AppMode.UniqueFolders, false) => "Уникальные папки",
                (AppMode.UniqueFolders, true) => "\uE8C0",
                (AppMode.Combined, false) => "Комбинированный",
                (AppMode.Combined, true) => "\uE8F1",
                _ => string.Empty
            };
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;

        /// <summary>
        /// A brush from a hex string, frozen: a freezable that a binding may keep a
        /// reference to has to be frozen before it is handed out.
        /// </summary>
        private static SolidColorBrush Brush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
            brush.Freeze();
            return brush;
        }
    }
}
