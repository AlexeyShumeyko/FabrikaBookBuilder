using System;
using System.Collections.Generic;

namespace PhotoBook.Application
{
    /// <summary>The JPEG names the program treats as photographs, in one place.</summary>
    /// <remarks>
    /// The list used to exist twice - once for picking a folder, once for what a site page
    /// takes - and the two disagreed on capital letters. Both ask here now, so a file the
    /// project lists is a file the site accepts.
    /// </remarks>
    public static class JpegFiles
    {
        /// <summary>Extensions a JPEG may have, matched without regard to case.</summary>
        public static IReadOnlyList<string> Extensions { get; } = new[] { ".jpg", ".jpeg" };

        /// <summary>Whether the name is a JPEG, judged by its extension alone.</summary>
        public static bool Is(string path)
        {
            var extension = string.IsNullOrEmpty(path) ? null : System.IO.Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension))
                return false;

            for (var index = 0; index < Extensions.Count; index++)
            {
                if (string.Equals(Extensions[index], extension, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
