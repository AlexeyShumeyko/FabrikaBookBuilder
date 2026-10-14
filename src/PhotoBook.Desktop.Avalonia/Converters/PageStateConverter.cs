using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PhotoBook.Core;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Shell.Converters
{
    /// <summary>
    /// The file list's state badge: one solid colour per state, white text on it, no frame.
    /// </summary>
    /// <remarks>
    /// Assigned is the green of "used", run-wide is the product's own colour because it is
    /// the one state that is the same in every book, and free is the faint grey of a state
    /// that needs no attention. Painted like a button, which is what it is.
    /// </remarks>
    public class PhotoUsageToBrushConverter : IValueConverter
    {
        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => value switch
            {
                PhotoUsage.Assigned => Solid("#10B981"),
                PhotoUsage.Shared => Solid("#6366F1"),
                _ => Solid("#94A3B8")
            };

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();

        private static IBrush Solid(string hex) => new SolidColorBrush(Color.Parse(hex));
    }

    /// <summary>
    /// What a slot is showing, as a value its bindings can use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It answers the questions the markup keeps asking about a page, because each of them is
    /// a conjunction or a colour rule and neither can be written as a binding here:
    /// </para>
    /// <list type="bullet">
    /// <item><b>hasphoto</b> and <b>empty</b> - a path is set, or it is not.</item>
    /// <item><b>imageerror</b> - a path is set but nothing could be read from it. This is the
    /// state that let "the photographs fell off" go unnoticed: an unreadable file and an
    /// empty slot look identical, and this is what tells them apart.</item>
    /// <item><b>slotbrush</b> - the accent colour for the slot's label, for a cover and for
    /// a run-wide photograph.</item>
    /// <item><b>filled</b> and <b>filledbrush</b> - a book's progress line, green once it is
    /// filled and red while it is not.</item>
    /// </list>
    /// </remarks>
    public class PageStateConverter : IValueConverter
    {
        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            var role = (parameter as string)?.ToLowerInvariant() ?? string.Empty;

            if (value is Page page)
            {
                return role switch
                {
                    "hasphoto" => !string.IsNullOrEmpty(page.SourcePath),
                    "empty" => string.IsNullOrEmpty(page.SourcePath),
                    "imageerror" => !string.IsNullOrEmpty(page.SourcePath) && !page.HasImage,
                    "slotbrush" => page.IsSlotLabelAccent ? Solid("#4F46E5") : Solid("#64748B"),
                    "slotempty" => page.IsCover ? "Обложка" : "Слот пуст",
                    _ => string.Empty
                };
            }

            if (value is Book book)
            {
                return role switch
                {
                    "filled" => book.IsFilled,
                    "filledbrush" => book.IsFilled ? Solid("#059669") : Solid("#EF4444"),

                    // The progress line goes semibold once it is done. The other shell does
                    // it with a trigger on a style; here it is a value, because the weight
                    // is a property rather than a brush.
                    "filledsemi" => book.IsFilled
                        ? global::Avalonia.Media.FontWeight.SemiBold
                        : global::Avalonia.Media.FontWeight.Normal,

                    _ => string.Empty
                };
            }

            return role == "structurecomplete" && value is bool complete
                ? Solid(complete ? "#059669" : "#EF4444")
                : string.Empty;
        }

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();

        private static IBrush Solid(string hex) => new SolidColorBrush(Color.Parse(hex));
    }
}
