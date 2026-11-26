using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace PhotoBook.Shell.Views
{
    /// <summary>
    /// Opens a slot's photograph with whatever Windows opens that kind of file with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The program keeps the path of every photograph it shows and nothing else about it, so
    /// the original is the machine's to display: a viewer inside this window would be another
    /// surface to keep sharp and another copy of a picture to decode, and the one already on
    /// the computer is the one the photographer knows.
    /// </para>
    /// <para>
    /// Nothing here is worth a dialog. A source that has gone away since the project was saved,
    /// or a file type the machine has no viewer for, leaves the double click unanswered: the
    /// person is looking at the photograph already, and a modal over their work would be
    /// louder than the missing file.
    /// </para>
    /// </remarks>
    internal static class OriginalPhoto
    {
        public static void Show(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                if (!File.Exists(path)) return;

                _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or Win32Exception or InvalidOperationException)
            {
                // Explained above: the photograph is on screen, and this is the machine's
                // viewer rather than something the program has to make right.
            }
        }
    }
}
