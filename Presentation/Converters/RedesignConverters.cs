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

            // The two modes each own a colour, used everywhere a mode is named: the chip
            // on a project card, the icon on the mode-select cards, the empty states.
            // Sky for the folders mode, the app's indigo for the combined one. The folders
            // mode was emerald first and the owner rejected it - emerald is "Готов к печати"
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

            (double w, double h) = ParseRatio(parameter as string);
            if (w <= 0d) return double.NaN;

            return Math.Round(width * h / w, 2);
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
    /// The fill of a photo's state badge in the file list: ONE saturated colour per state,
    /// white text on top, no border. That is the whole design.
    ///
    /// The owner asked for it four times and the fourth answer is this one, in his words:
    /// "like you paint a button". The three that came before it are the reason the roles are
    /// gone rather than extended:
    ///
    ///   1. a tint over the whole row - "I asked for the status, not the cell";
    ///   2. a coloured frame only - "barely visible";
    ///   3. a `-100` tint with dark text and a frame - "there is NO fill", which is what a
    ///      pale tint on a 10px label looks like: white with a hint, so all three states
    ///      read as grey-with-a-border and none of them reads as its own colour.
    ///
    /// So there is exactly one brush, it is saturated, and the label is white on it. A
    /// converter with a "border" and a "text" role is what let attempt 2 and attempt 3 exist
    /// side by side and disagree with attempt 1; there is nothing left here to disagree.
    /// Free is slate because it needs no attention, assigned is the green of "done", and
    /// shared is the brand colour - the one state the photographer has to notice, since the
    /// same picture is going into every book.
    /// </summary>
    public class PhotoUsageToBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // `{Binding Usage, Converter=...}` hands over the VALUE of Usage - the enum -
            // not the PhotoFileInfo it came from. An `is PhotoFileInfo` test therefore never
            // matched, every state fell through to "free", and all three badges were the
            // same grey while their labels said "Общий", "Назначен" and "Свободен". The
            // owner saw that as "it is outlined and filled with grey" and was right: the
            // colour was grey, always. Both shapes are accepted so the next binding style
            // cannot reintroduce it.
            var usage = value switch
            {
                PhotoUsage u => u,
                PhotoFileInfo file => file.Usage,
                _ => PhotoUsage.Free
            };

            return usage switch
            {
                PhotoUsage.Assigned => Brush(0x10, 0xB9, 0x81),   // emerald-500
                PhotoUsage.Shared   => Brush(0x63, 0x66, 0xF1),   // brand-500
                _                    => Brush(0x94, 0xA3, 0xB8)    // slate-400
            };
        }

        private static System.Windows.Media.SolidColorBrush Brush(byte r, byte g, byte b)
            => new(System.Windows.Media.Color.FromRgb(r, g, b));

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// Card size for a spread card: the photo frame's width and height, adapted to how
    /// much room the book block actually has.
    ///
    /// Natural size: <see cref="DefaultNaturalHeight"/> tall, and that times the book's
    /// own format wide (<see cref="Page.FrameAspect"/>). The height is the mockup's value
    /// on purpose - the card's shape is the one thing a photographer cannot work with, so
    /// the format decides the width and never the height.
    ///
    /// Adaptation: if a whole extra card does not fit in the row but the leftover space is
    /// worth spending, the card is scaled by exactly the factor that makes it fit, both
    /// sides together, so the format and the card's proportions are preserved. The decision
    /// uses ONLY the available width, never the number of items, so a half-empty last row
    /// cannot change the size.
    ///
    /// The row is filled, always. There is no cap on how far a card may shrink: the only
    /// limit is <see cref="MinWidth"/>. A leftover bigger than <see cref="MaxLeftover"/> is
    /// a hole in the layout that the photographer reads as a bug - the owner reported
    /// exactly that, twice: two cards in a row with a fifth of the width empty beside them.
    /// The proportional cap that used to be here (20%) is what produced it.
    ///
    /// Usage: MultiBinding of the row area's width and the Page, with
    /// ConverterParameter="width" or "height". A second comma-separated number overrides
    /// <see cref="DefaultNaturalHeight"/> for that binding, i.e. "width,150".
    ///
    /// The override exists because the two modes do not have the same room. The
    /// unique-folder mode gives its cards the whole window, and 190px is the size the
    /// mockup was drawn at. The combined mode shares the window with a 320px file list,
    /// so its design height is smaller.
    ///
    /// The width must be EXACT, and it must be something the cards cannot influence. The
    /// row area is the slots' own <c>ItemsControl</c> - a vertical stack under a
    /// <c>ScrollViewer</c> hands its children a fixed width, so that width never depends on
    /// how wide the cards came out, and the scrollbar is already subtracted from it.
    /// Deriving it instead (ScrollViewer's own width minus a hand-picked reserve) was
    /// wrong by 10px, and 10px is a whole card: the rule sized the cards for three per row
    /// and the row then fitted two, leaving a fifth of the width empty.
    /// </summary>
    public class AdaptiveCardSizeConverter : IMultiValueConverter
    {
        /// <summary>Mockup card height. See doc/DESIGN_SPEC.md 5.2.</summary>
        protected const double DefaultNaturalHeight = 190d;

        /// <summary>Gap between cards. Must match SpreadCard's Margin in the view.</summary>
        private const double CardMargin = 16d;

        /// <summary>
        /// What the card adds around the photo frame: a 1px border on each side. The
        /// WrapPanel packs the CARD, not the frame, and leaving this out costs exactly the
        /// last card of the row.
        /// </summary>
        private const double CardChrome = 2d;

        /// <summary>
        /// How much dead space may be left on the right before the cards are scaled down
        /// to win one more per row. A flat few pixels, not a share of a step: a fifth of a
        /// step is a fifth of the row, and a hole that size is exactly what the owner
        /// reported as "the row does not fill". The design size is kept only when the row
        /// is already flush.
        /// </summary>
        private const double MaxLeftover = 12d;

        /// <summary>
        /// Floor set by what the card has to SAY, not by taste: at rest the footer is one
        /// line - "Разворот 1 (общий)" plus the bin - and on hover that label is REPLACED
        /// by the "Во все книги" word rather than pushed aside, so 18px of padding, 22px of
        /// bin and ~80px of word is the whole budget: 130px holds it. A very portrait
        /// format - 0.67 post-prints - is 100px wide at the design height, so such a book
        /// gets a LARGER frame rather than a clipped caption. The frame keeps the book's
        /// aspect either way; only its size changes.
        /// </summary>
        private const double MinWidth = 130d;

        /// <summary>Upper clamp, so an extreme panorama cannot become a banner.</summary>
        private const double MaxWidth = 480d;

        private const double FallbackAspect = 1.6d;   // the mockup's 16/10

        public object Convert(object?[]? values, Type targetType, object? parameter, CultureInfo culture)
        {
            double available = values != null && values.Length > 0 ? ToDouble(values[0]) : 0d;
            double aspect = values != null && values.Length > 1 && values[1] is Page page && page.FrameAspect > 0d
                ? page.FrameAspect
                : FallbackAspect;

            bool wantHeight = false;
            double naturalHeight = DefaultNaturalHeight;
            var parts = (parameter as string)?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        ?? Array.Empty<string>();
            if (parts.Length > 0)
                wantHeight = string.Equals(parts[0].Trim(), "height", StringComparison.OrdinalIgnoreCase);
            if (parts.Length > 1 &&
                double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double h) &&
                h > 0d)
                naturalHeight = h;

            double naturalWidth = naturalHeight * aspect;
            double width = FitWidth(available, naturalWidth);

            return Math.Round(wantHeight ? width / aspect : width, 1);
        }

        /// <summary>
        /// The width to lay out with. <paramref name="naturalWidth"/> is the designed size;
        /// the answer is that size scaled down by the factor that fits one more card per
        /// row, when such a factor exists without breaking anything.
        /// </summary>
        private static double FitWidth(double available, double naturalWidth)
        {
            double natural = Math.Clamp(naturalWidth, MinWidth, MaxWidth);

            // First layout pass: nothing is measured yet, so start from the design size.
            if (available <= 0d) return natural;

            // A window narrower than one card: fill the width rather than overflow it.
            if (natural + CardChrome + CardMargin > available)
                return Math.Max(available - CardChrome - CardMargin, MinWidth);

            // WrapPanel wraps as soon as the NEXT item's full step no longer fits, and that
            // step is the whole CARD: frame + border on both sides + right margin. N cards
            // need N * step <= available. Counting the step without the card's border is
            // what left a card short of the edge.
            double step = natural + CardChrome + CardMargin;
            int perRow = (int)(available / step);
            if (perRow < 1) perRow = 1;

            double leftover = available - perRow * step;
            if (leftover <= MaxLeftover) return natural;   // already flush, leave it alone

            // Frame width that would fit one more card per row, with a pixel of slack so
            // floating point rounding cannot push the last item over the edge.
            double target = available / (perRow + 1) - CardChrome - CardMargin - 1d;

            // The floor is the ONLY limit: below it the caption stops being readable, and
            // above it the row is filled whatever that costs. A second, proportional limit
            // used to sit here and it is what left two cards in a row with a fifth of the
            // width empty - the exact hole the owner reported.
            if (target >= MinWidth && target < natural)
                return target;

            return natural;
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
