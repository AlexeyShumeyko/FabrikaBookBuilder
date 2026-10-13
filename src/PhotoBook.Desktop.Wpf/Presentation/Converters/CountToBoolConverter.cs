using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PhotoBook.Core;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBookRenamer.Presentation.Converters
{
    /// <summary>
    /// Collection count > 0 -> true. Works with any IEnumerable.
    /// Pass ConverterParameter="invert" to flip it, which is how empty states are shown.
    ///
    /// Returns a <see cref="Visibility"/> when the binding target is a Visibility property:
    /// WPF will not coerce a bool to Visibility on its own, so returning a raw bool there
    /// silently leaves the element Visible.
    /// </summary>
    public class CountToBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool result = HasItems(value);
            if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
                result = !result;

            return targetType == typeof(Visibility)
                ? (result ? Visibility.Visible : Visibility.Collapsed)
                : result;
        }

        private static bool HasItems(object? value)
        {
            switch (value)
            {
                case null: return false;
                case string s: return !string.IsNullOrEmpty(s);
                // Numeric counts are treated as "is it greater than zero", so the same
                // converter works for both a collection and a ProjectsCount-style int.
                case int i: return i > 0;
                case long l: return l > 0;
                case double d: return d > 0;
                case System.Collections.ICollection c: return c.Count > 0;
                case System.Collections.IEnumerable e:
                    var en = e.GetEnumerator();
                    try { return en.MoveNext(); }
                    finally { (en as IDisposable)?.Dispose(); }
                default: return false;
            }
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
