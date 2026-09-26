using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PhotoBookRenamer.Domain;

namespace PhotoBookRenamer.Presentation.Converters
{
    /// <summary>
    /// "1 обложка + 5 разворотов" / "5 разворотов" / "пусто" for a <see cref="Book"/>.
    /// </summary>
    public class BookSummaryConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not Book book)
                return "пусто";

            int spreads = 0;
            if (book.Pages != null)
            {
                foreach (var p in book.Pages)
                    if (p != null && !p.IsCover) spreads++;
            }

            bool hasCover = book.Cover != null && !book.Cover.IsEmpty;
            if (spreads == 0 && !hasCover) return "пусто";
            if (spreads == 0) return "только обложка";

            string spreadsText = Plural(spreads, "разворот", "разворота", "разворотов");
            return hasCover ? $"1 обложка + {spreadsText}" : $"{spreadsText}";
        }

        private static string Plural(int n, string one, string few, string many)
        {
            int mod10 = n % 10;
            int mod100 = n % 100;
            if (mod10 == 1 && mod100 != 11) return one;
            if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return few;
            return many;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// "Готово 2 из 4" - how many slots of a book are filled.
    /// </summary>
    public class BookProgressConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not Book book) return "0 из 0";

            int filled = 0, total = 0;
            if (book.Cover != null) { total++; if (!book.Cover.IsEmpty) filled++; }
            if (book.Pages != null)
            {
                foreach (var p in book.Pages)
                {
                    if (p == null || p.IsCover) continue;
                    total++;
                    if (!p.IsEmpty) filled++;
                }
            }
            return $"Готово {filled} из {total}";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// 0..1 ratio to "42%". Used for the readiness chip width and the export progress label.
    /// </summary>
    public class DoubleToPercentConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            double d = value switch
            {
                double dd => dd,
                int ii => ii,
                float ff => ff,
                decimal mm => (double)mm,
                _ => 0d
            };
            return $"{Math.Round(Math.Clamp(d, 0d, 1d) * 100d):0}%";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// ProjectStatus -> pill background brush (emerald / amber / slate, per the design).
    /// </summary>
    public class StatusToBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var status = value is ProjectStatus s ? s : ProjectStatus.NotFilled;
            var hex = status switch
            {
                ProjectStatus.SuccessfullyCompleted => "#ECFDF5", // emerald-50
                ProjectStatus.Ready => "#FFFBEB",                   // amber-50
                _ => "#F1F5F9"                                     // slate-100
            };
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

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

    /// <summary>
    /// "Изменён: 24 мая, 14:20" - relative for today/yesterday, absolute otherwise.
    /// </summary>
    public class LastModifiedTextConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not DateTime dt) return string.Empty;

            var now = DateTime.Now;
            string body;
            if (dt.Date == now.Date)
                body = dt.ToString("HH:mm", CultureInfo.InvariantCulture);
            else if (dt.Date == now.Date.AddDays(-1))
                body = "вчера, " + dt.ToString("HH:mm", CultureInfo.InvariantCulture);
            else if (dt.Year == now.Year)
                body = dt.ToString("d MMM, HH:mm", new CultureInfo("ru-RU"));
            else
                body = dt.ToString("d MMM yyyy", new CultureInfo("ru-RU"));

            return string.Equals(parameter as string, "prefix", StringComparison.OrdinalIgnoreCase)
                ? "Изменён: " + body
                : body;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>int -> string, for binding a column count to UniformGrid.Columns.</summary>
    public class IntToStringConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value?.ToString() ?? "1";

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => int.TryParse(value?.ToString(), out int i) ? i : 1;
    }

    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool has = value is string s ? !string.IsNullOrWhiteSpace(s)
                     : value is System.Collections.ICollection c ? c.Count > 0
                     : value != null;
            return has ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    public class InverseStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool has = value is string s ? !string.IsNullOrWhiteSpace(s)
                     : value is System.Collections.ICollection c ? c.Count > 0
                     : value != null;
            return has ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>Non-null / non-empty -> true. Drives filled vs empty slot visuals.</summary>
    public class HasValueConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string s) return !string.IsNullOrWhiteSpace(s);
            if (value is System.Collections.ICollection c) return c.Count > 0;
            return value != null;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>Collection count > 0 -> true. Works with any IEnumerable.</summary>
    public class CountToBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is null) return false;
            if (value is string s) return !string.IsNullOrEmpty(s);
            if (value is System.Collections.ICollection c) return c.Count > 0;
            if (value is System.Collections.IEnumerable e)
            {
                var en = e.GetEnumerator();
                try { return en.MoveNext(); }
                finally { (en as IDisposable)?.Dispose(); }
            }
            return false;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>total - used, clamped at zero.</summary>
    public class SubtractConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            int total = ToInt(value);
            int sub = ToInt(parameter);
            return Math.Max(0, total - sub);
        }

        private static int ToInt(object? o) => o switch
        {
            int i => i,
            double d => (int)d,
            string s => int.TryParse(s, out int r) ? r : 0,
            _ => 0
        };

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>True when any bound flag is true. Accepts a params bool[] binding.</summary>
    public class AnyTrueConverter : IMultiValueConverter
    {
        public object Convert(object?[]? values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values == null) return false;
            foreach (var v in values)
            {
                if (v is bool b && b) return true;
            }
            return false;
        }

        public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
            => Array.Empty<object?>();
    }
}
