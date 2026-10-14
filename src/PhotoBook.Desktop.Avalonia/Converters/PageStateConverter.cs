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
        /// <summary>The roles whose answer is a colour.</summary>
        private static readonly string[] BrushRoles = { "slotbrush", "filledbrush", "structurecomplete" };

        /// <summary>What a colour role answers when nothing is known about the state.</summary>
        private static readonly IBrush FallbackBrush = new SolidColorBrush(Color.Parse("#64748B"));

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
                    _ => Fallback(role)
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

                    _ => Fallback(role)
                };
            }

            // The first book in the run carries no line above it. Every one after it does:
            // without the line, two books read as two separate things, and the eye stops
            // at the gap and never notices there is a third.
            if (role == "notfirst" && value is int index) return index > 1;

            if (role == "structurecomplete" && value is bool complete)
                return Solid(complete ? "#059669" : "#EF4444");

            return Fallback(role);
        }

        /// <summary>
        /// What to answer with when the state does not match the question.
        /// </summary>
        /// <remarks>
        /// A colour role always answers with a colour. It used to answer with an empty
        /// string, and an empty string where a colour belongs is not an empty colour: the
        /// engine tries to parse it, fails, and takes the whole program down. That is how a
        /// project that happened to have a book with nothing in it stopped the program
        /// opening, and it failed with no message and nothing in the journal.
        ///
        /// The question "what colour is this?" has an answer even when the state is not
        /// known, and giving it is the difference between a neutral grey and a crash.
        /// </remarks>
        private static object? Fallback(string role) =>
            Array.IndexOf(BrushRoles, role) >= 0 ? FallbackBrush : null;

        public object? ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
            => throw new NotSupportedException();

        private static IBrush Solid(string hex) => new SolidColorBrush(Color.Parse(hex));
    }
}
