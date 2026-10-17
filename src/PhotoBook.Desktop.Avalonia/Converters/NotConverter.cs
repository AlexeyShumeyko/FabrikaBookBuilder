using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PhotoBook.Shell.Converters
{
    /// <summary>
    /// A bool, the other way round.
    /// </summary>
    /// <remarks>
    /// It exists for one mark in the header: the two screens that cannot be reached without
    /// a project open, which say so with a small padlock that has to appear and disappear
    /// with the same flag that disables the tab. Reading "not" in the view model and
    /// exposing it as its own property would put a second source of truth next to the
    /// first for the sake of one glyph.
    /// </remarks>
    public class NotConverter : IValueConverter
    {
        public static readonly NotConverter Instance = new();

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => value is bool b ? !b : false;

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();
    }
}