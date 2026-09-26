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
    /// ProjectStatus -> one of the three colours of the status chip on a project card.
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
                (ProjectStatus.SuccessfullyCompleted, "text")   => "#047857", // emerald-700
                (ProjectStatus.SuccessfullyCompleted, _)        => "#ECFDF5", // emerald-50

                (ProjectStatus.Ready, "border") => "#FDE68A",                 // amber-200
                (ProjectStatus.Ready, "text")   => "#B45309",                 // amber-700
                (ProjectStatus.Ready, _)        => "#FFFBEB",                 // amber-50

                (_, "border") => "#E2E8F0",                                   // slate-200
                (_, "text")   => "#334155",                                   // slate-700
                _             => "#F1F5F9"                                    // slate-100
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

    /// <summary>
    /// Non-null / non-empty -> true. Drives filled vs empty slot visuals.
    /// Returns a <see cref="Visibility"/> when the target is a Visibility property.
    /// </summary>
    public class HasValueConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool result;
            if (value is string s) result = !string.IsNullOrWhiteSpace(s);
            else if (value is System.Collections.ICollection c) result = c.Count > 0;
            else result = value != null;

            return targetType == typeof(Visibility)
                ? (result ? Visibility.Visible : Visibility.Collapsed)
                : result;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

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

    /// <summary>
    /// Two-way AppMode &lt;-&gt; bool, so the header tabs can be RadioButtons bound straight
    /// to <c>MainViewModel.CurrentMode</c>.
    ///
    /// ConvertBack deliberately ignores <c>false</c>: when the mode changes, every other tab
    /// flips to unchecked, and returning a mode for that would immediately undo the change.
    /// </summary>
    public class AppModeEqualsConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not AppMode current) return false;
            if (parameter is not string name) return false;
            return Enum.TryParse(name, ignoreCase: true, out AppMode target) && current == target;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not bool isChecked || !isChecked) return Binding.DoNothing;
            if (parameter is not string name) return Binding.DoNothing;
            return Enum.TryParse(name, ignoreCase: true, out AppMode target) ? target : (object?)Binding.DoNothing;
        }
    }

    /// <summary>Inverse of <see cref="HasValueConverter"/>.</summary>
    public class InverseHasValueConverter : IValueConverter
    {
        private static readonly HasValueConverter Inner = new HasValueConverter();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool result = (bool)Inner.Convert(value, typeof(bool), parameter, culture);
            return targetType == typeof(Visibility)
                ? (result ? Visibility.Collapsed : Visibility.Visible)
                : !result;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// Marks a pool photo as already used by a slot of the selected book.
    ///
    /// This must be an <see cref="IMultiValueConverter"/>: the obvious
    /// <c>Binding="{Binding VM, Converter=..., ConverterParameter={Binding}}"</c> form
    /// throws at runtime ("Binding cannot be set on the property Binding of type
    /// Binding") because a nested Binding is not allowed there. A MultiBinding passes the
    /// two values as an array instead.
    ///
    /// values[0] = the ViewModel, values[1] = the file path of this pool tile.
    /// </summary>
    public class PoolItemUsageConverter : IMultiValueConverter
    {
        public object Convert(object?[]? values, Type targetType, object? parameter, CultureInfo culture)
        {
            bool used = false;

            if (values != null && values.Length >= 2
                && values[0] is Presentation.ViewModels.CombinedModeViewModel vm
                && values[1] is string path && !string.IsNullOrEmpty(path))
            {
                var book = vm.SelectedBook;
                if (book != null)
                {
                    if (book.Cover != null && PathsEqual(book.Cover.SourcePath, path)) used = true;
                    if (!used)
                    {
                        foreach (var p in book.Pages)
                        {
                            if (p != null && !p.IsCover && PathsEqual(p.SourcePath, path)) { used = true; break; }
                        }
                    }
                }
            }

            return targetType == typeof(Visibility)
                ? (used ? Visibility.Visible : Visibility.Collapsed)
                : used;
        }

        private static bool PathsEqual(string? a, string b)
            => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
            => Array.Empty<object?>();
    }

    /// <summary>
    /// Page -&gt; the role label a slot header shows: "ОБЛОЖКА" for the cover, otherwise
    /// "Разворот N". Takes the whole Page rather than a bool because the mockup numbers the
    /// spreads ("Разворот 1", "Разворот 2"), which a bool cannot produce.
    /// </summary>
    public class SlotRoleConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not Page page) return string.Empty;
            return page.IsCover ? "ОБЛОЖКА" : $"Разворот {page.Index}";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
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

            return (mode, wantIcon) switch
            {
                (AppMode.UniqueFolders, false) => "Уникальные папки",
                (AppMode.UniqueFolders, true) => "\uE8C0",
                (AppMode.Combined, false) => "Комбинированный",
                (AppMode.Combined, true) => "\uE71D",
                _ => string.Empty
            };
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// Reproduces Tailwind's <c>aspect-[16/10]</c>, which the mockup uses for every image
    /// frame. WPF has no aspect-ratio layout, so the height is derived from the measured width.
    ///
    /// Usage: bind an element's Height to a MultiBinding of that element's ActualWidth and
    /// its own DataContext, with ConverterParameter="16,10" on the target.
    ///
    /// A hardcoded pixel height was why the first pass looked wrong: fixed-width cards with
    /// fixed-height images never matched the mockup's proportions.
    /// </summary>
    public class AspectHeightConverter : IMultiValueConverter
    {
        public object Convert(object?[]? values, Type targetType, object? parameter, CultureInfo culture)
        {
            double width = values != null && values.Length > 0 ? ToDouble(values[0]) : 0d;
            if (width <= 0d) return double.NaN;   // fall back to whatever the layout gives

            (double w, double h) = ResolveRatio(values, parameter);
            if (w <= 0d) return double.NaN;

            return Math.Round(width * h / w, 2);
        }

        /// <summary>
        /// A Page in the second slot carries the frame ratio its book decided
        /// (<see cref="Page.FrameAspect"/>), which is how a card ends up shaped like its
        /// own print format. Everything else - and any page without a known ratio, such
        /// as the combined editor - falls back to the ConverterParameter.
        /// </summary>
        private static (double, double) ResolveRatio(object?[]? values, object? parameter)
        {
            if (values != null && values.Length > 1 && values[1] is Page page && page.FrameAspect > 0d)
                return (1d, page.FrameAspect);

            return ParseRatio(parameter as string);
        }

        /// <summary>Reads "16,10" (or "16/10") into a width:height pair; defaults to 16:10.</summary>
        private static (double, double) ParseRatio(string? spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return (16d, 10d);

            var parts = spec.Replace('/', ',').Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return (16d, 10d);

            if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double w) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double h))
            {
                return (w, h);
            }
            return (16d, 10d);
        }

        private static double ToDouble(object? o) => o switch
        {
            double d => d,
            int i => i,
            float f => f,
            _ => 0d
        };

        public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
            => Array.Empty<object?>();
    }

    /// <summary>
    /// A rounded <see cref="RectangleGeometry"/> for a <c>Geometry</c> property, built from
    /// the target's own ActualWidth / ActualHeight and a radius from the
    /// ConverterParameter.
    ///
    /// Why this exists: <c>Border.ClipToBounds</c> clips to the bounding RECTANGLE, not
    /// to the corner radius. A photo flush with a rounded card therefore paints its square
    /// corners over the rounded border. The card's whole content - photo and footer -
    /// needs one rounded clip, which also trims the hover zoom.
    ///
    /// Self-referencing ActualWidth is safe here: Clip does not take part in measure, so
    /// there is no layout cycle (unlike a value that measure depends on).
    /// </summary>
    public class RoundedClipGeometryConverter : IMultiValueConverter
    {
        public object? Convert(object?[]? values, Type targetType, object? parameter, CultureInfo culture)
        {
            double width = values != null && values.Length > 0 ? AspectHeightConverterValue(values[0]) : 0d;
            double height = values != null && values.Length > 1 ? AspectHeightConverterValue(values[1]) : 0d;

            // Nothing sensible to clip to yet: let the layout settle, then clip.
            if (width <= 0d || height <= 0d) return null;

            double radius = 12d;
            if (double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                radius = parsed;

            // A radius larger than half the shorter side renders as a malformed geometry.
            radius = Math.Min(radius, Math.Min(width, height) / 2d);

            return new System.Windows.Media.RectangleGeometry(
                new System.Windows.Rect(0, 0, width, height), radius, radius);
        }

        private static double AspectHeightConverterValue(object? o) => o switch
        {
            double d => d,
            int i => i,
            float f => f,
            _ => 0d
        };

        public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
            => Array.Empty<object?>();
    }
}
