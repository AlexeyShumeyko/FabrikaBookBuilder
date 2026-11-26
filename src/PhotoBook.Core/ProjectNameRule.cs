using System;
using System.Globalization;

namespace PhotoBook.Core
{
    /// <summary>
    /// What a project may be called, and what it is called when nobody named it.
    /// </summary>
    /// <remarks>
    /// Twenty-five characters, spaces included. The limit is a rule about the name rather than
    /// about the box it is typed in, so the one place that answers it is here. A name already
    /// written into an older project is left as it is: shortening somebody's project on the
    /// way in would be a rename nobody asked for.
    /// </remarks>
    public static class ProjectNameRule
    {
        /// <summary>How many characters a name may have, spaces counted.</summary>
        public const int MaxLength = 25;

        /// <summary>
        /// The name cut to what a project may be called.
        /// </summary>
        /// <remarks>
        /// Cut by text elements, not by UTF-16 units: half an emoji or a letter left without
        /// its accent is not a shorter name, it is a broken one. A name that already fits is
        /// handed back untouched, so nothing is trimmed on the way through.
        /// </remarks>
        public static string Limit(string? name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            if (name.Length <= MaxLength) return name;

            var text = new StringInfo(name);
            return text.LengthInTextElements <= MaxLength
                ? name
                : text.SubstringByTextElements(0, MaxLength);
        }

        /// <summary>
        /// The name a project gets when nobody gave it one: the date and the time it started.
        /// </summary>
        public static string Unnamed(DateTime at) => at.ToString("yyyy-MM-dd HH:mm");
    }
}
