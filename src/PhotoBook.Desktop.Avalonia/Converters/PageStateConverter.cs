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

        /// <summary>
        /// The roles that answer with a value the markup would otherwise have to compute.
        /// </summary>
        /// <remarks>
        /// The three of them read a bool - a label that is accented, a book that is filled -
        /// rather than the object those facts belong to. That is not a detail: bound to the
        /// object, the question is answered once, when the card is built, and a spread that
        /// becomes run-wide afterwards kept its grey label and a book that fills up kept its
        /// red counter. Bound to the fact, it is answered when the fact changes.
        /// </remarks>
        private static (bool Known, object? Answer) FromFlag(string role, bool flag) => role switch
        {
            // The slot's own state, read from a property that changes when it changes. Bound
            // to the page itself these were answered once, when the card was built, and a
            // photograph dropped into a slot stayed invisible behind the "slot is empty"
            // frame - which is why the screen used to rebuild the whole list on every drop.
            "hasphoto" => (true, !flag),      // IsEmpty
            "empty" => (true, flag),          // IsEmpty
            "imageerror" => (true, !flag),    // HasImage

            "slotbrush" => (true, Solid(flag ? "#4F46E5" : "#64748B")),
            "slotweight" => (true, flag ? FontWeight.Bold : FontWeight.Normal),
            "filledbrush" => (true, Solid(flag ? "#059669" : "#EF4444")),
            "filledsemi" => (true, flag ? FontWeight.SemiBold : FontWeight.Normal),
            "structurecomplete" => (true, Solid(flag ? "#059669" : "#EF4444")),

            // The empty slot's own caption, decided from IsCover. It used to be asked for with
            // the bool and answered only for a page - so the bold line in the middle of an
            // empty frame was never there at all, and the frame said nothing but "drop a photo
            // here" in ten-point grey.
            "slotempty" => (true, flag ? "Обложка" : "Слот пуст"),

            _ => (false, null)
        };

        /// <summary>What a colour role answers when nothing is known about the state.</summary>
        private static readonly IBrush FallbackBrush = new SolidColorBrush(Color.Parse("#64748B"));

        public object? Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            var role = (parameter as string)?.ToLowerInvariant() ?? string.Empty;

            if (value is bool flag)
            {
                var (known, answer) = FromFlag(role, flag);
                if (known) return answer;
            }

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

            // The first book in the run carries no line above it. Every one after it does:
            // without the line, two books read as two separate things, and the eye stops
            // at the gap and never notices there is a third.
            if (role == "notfirst" && value is int index) return index > 1;

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
