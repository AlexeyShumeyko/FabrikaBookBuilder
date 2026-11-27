using System;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>One line of the menu a book's three dots open.</summary>
    /// <remarks>
    /// The prototype's menu is a small card of named destinations, each with the format it
    /// belongs to under it, and one red line at its foot. The entry carries the words and the
    /// mark rather than a command, because the page changes the menu after every move: it is
    /// built at the moment the dots are pressed, from the groups that exist then.
    /// </remarks>
    public sealed record BookMenuEntry(
        string Title,
        string Subtitle,
        string Icon,
        bool IsDanger,
        bool IsSeparator,
        Action? Run)
    {
        /// <summary>A line of the menu: a title, the format under it, and what pressing it does.</summary>
        public static BookMenuEntry Item(
            string title,
            string subtitle,
            string icon,
            Action run,
            bool danger = false) =>
            new(title, subtitle, icon, danger, false, run);

        /// <summary>The hairline the prototype draws between moving a book and excluding it.</summary>
        public static BookMenuEntry Separator() =>
            new(string.Empty, string.Empty, string.Empty, false, true, null);
    }
}
