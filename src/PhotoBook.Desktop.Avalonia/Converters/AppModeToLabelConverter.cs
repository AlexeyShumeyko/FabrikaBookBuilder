using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
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

            if (role is not null && role.StartsWith("tint", StringComparison.OrdinalIgnoreCase))
            {
                bool folders = mode == AppMode.UniqueFolders;
                return role.ToLowerInvariant() switch
                {
                    "tintbg" => StatusToLabelConverter.Brush(folders ? "#F0F9FF" : "#EEF2FF"),
                    "tintborder" => StatusToLabelConverter.Brush(folders ? "#BAE6FD" : "#C7D2FE"),
                    "tinttext" => StatusToLabelConverter.Brush(folders ? "#0369A1" : "#4338CA"),
                    "tinticon" => StatusToLabelConverter.Brush(folders ? "#0284C7" : "#4F46E5"),
                    _ => StatusToLabelConverter.Brush("#00000000")
                };
            }

            return (mode, wantIcon) switch
            {
                (AppMode.UniqueFolders, false) => "Уникальные папки",
                (AppMode.UniqueFolders, true) => "\uE8C0",   // the folder
                (AppMode.Combined, false) => "Комбинированный",
                (AppMode.Combined, true) => "\uE8F1",     // books on a shelf
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
