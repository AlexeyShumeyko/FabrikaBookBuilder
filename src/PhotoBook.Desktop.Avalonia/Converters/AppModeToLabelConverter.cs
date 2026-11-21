using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PhotoBook.Shell.Services;
using PhotoBook.Core;

namespace PhotoBook.Shell.Converters
{
    /// <summary>
    /// AppMode - what a saved project's card says about the mode it was made in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The chip is not decoration: a saved project is only reachable through its mode, so
    /// the mode has to be visible before it is opened.
    /// </para>
    /// <para>
    /// The parameter picks the shape, the same way the other converters do: "icon" returns
    /// the codepoint, "visibility" collapses the chip for a record saved before a mode was
    /// stamped - an empty chip would read as a bug rather than as missing data.
    /// </para>
    /// <para>
    /// The two modes each own a colour wherever a mode is named. The folders mode was
    /// emerald first and rejected, then grey, for the same reason: emerald and grey already
    /// mean statuses, and a mode chip has to stand out from them.
    /// </para>
    /// </remarks>
    public class AppModeToLabelConverter : IValueConverter
    {
        public static readonly AppModeToLabelConverter Instance = new();

        /// <summary>The mode's mark, drawn rather than typed: the two glyphs the platform
        /// carried before were a folder and a bookshelf, and the prototype's are a grid of
        /// four leaves and a folder with a flap. Sizes stay where the chip had them.</summary>
        private static readonly Geometry UniqueIcon =
            Geometry.Parse("M3.5 8A2.5 2.5 0 0 1 6 5.5h3.6l1.8 2.2H18a2.5 2.5 0 0 1 2.5 2.5v7A2.5 2.5 0 0 1 18 19.7H6A2.5 2.5 0 0 1 3.5 17.2z");

        private static readonly Geometry CombinedIcon =
            Geometry.Parse("M5.3 3.5h3.4a1.8 1.8 0 0 1 1.8 1.8v3.4a1.8 1.8 0 0 1-1.8 1.8H5.3a1.8 1.8 0 0 1-1.8-1.8V5.3a1.8 1.8 0 0 1 1.8-1.8zM15.3 3.5h3.4a1.8 1.8 0 0 1 1.8 1.8v3.4a1.8 1.8 0 0 1-1.8 1.8h-3.4a1.8 1.8 0 0 1-1.8-1.8V5.3a1.8 1.8 0 0 1 1.8-1.8zM5.3 13.5h3.4a1.8 1.8 0 0 1 1.8 1.8v3.4a1.8 1.8 0 0 1-1.8 1.8H5.3a1.8 1.8 0 0 1-1.8-1.8v-3.4a1.8 1.8 0 0 1 1.8-1.8zM15.3 13.5h3.4a1.8 1.8 0 0 1 1.8 1.8v3.4a1.8 1.8 0 0 1-1.8 1.8h-3.4a1.8 1.8 0 0 1-1.8-1.8v-3.4a1.8 1.8 0 0 1 1.8-1.8z");

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            if (value is not AppMode mode) return string.Empty;

            var role = parameter as string;

            // A plain bool, not a Visibility: Avalonia's IsVisible takes a bool, and the WPF
            // equivalent of this branch produced a Visibility because WPF's IsVisible took
            // one. The conversion happened to be invisible in the markup and is not
            // invisible in the layer.
            if (StatusToLabelConverter.Is(role, "visibility"))
                return mode is AppMode.UniqueFolders or AppMode.Combined;

            bool wantIcon = StatusToLabelConverter.Is(role, "icon");

            if (StatusToLabelConverter.Is(role, "iconpath"))
            {
                return mode switch
                {
                    AppMode.UniqueFolders => UniqueIcon,
                    AppMode.Combined => CombinedIcon,
                    _ => null
                };
            }

            if (role is not null && role.StartsWith("tint", StringComparison.OrdinalIgnoreCase))
            {
                return role.ToLowerInvariant() switch
                {
                    "tintbg" => ThemeResources.Brush("Sunken"),
                    "tintborder" => ThemeResources.Brush("BorderSubtle"),
                    "tinttext" => ThemeResources.Brush("TextDefault"),
                    "tinticon" => mode == AppMode.UniqueFolders
                        ? ThemeResources.Brush("ModeIconUnique")
                        : ThemeResources.Brush("Accent"),
                    _ => Brushes.Transparent
                };
            }

            return (mode, wantIcon) switch
            {
                (AppMode.UniqueFolders, false) => "Уникальные папки",
                (AppMode.Combined, false) => "Комбинированный",
                _ => string.Empty
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
