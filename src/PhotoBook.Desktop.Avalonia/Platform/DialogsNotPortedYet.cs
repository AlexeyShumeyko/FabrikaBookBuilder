using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Core;

namespace PhotoBook.Shell.Platform
{
    /// <summary>
    /// A stand-in for the windows that collect an answer, so that an editor can be built
    /// before those windows exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a named gap, not a working fallback. The export options window, the folder
    /// browser and the page-number prompt are step 3.5; until then the editors cannot be
    /// constructed at all without this, and a shell that starts with no screen is not a shell.
    /// </para>
    /// <para>
    /// It fails loudly rather than returning a plausible answer, because a dialog that
    /// silently returns "cancelled" would let someone believe they had exported when nothing
    /// happened. The message names the step so the gap is obvious from the crash rather than
    /// from a bug report.
    /// </para>
    /// </remarks>
    internal class DialogsNotPortedYet : IEditorDialogs
    {
        private static NotSupportedException Missing() =>
            new("This shell has no such window yet - it is wave 3, step 3.5.");

        public Task<string?> AskToExport(Project project) => throw Missing();

        public Task ShowExportSucceeded(string outputFolder) => throw Missing();

        public List<string>? PickFolders(List<string> existingFolders) => throw Missing();

        public int? AskPageNumber(int maxPageNumber) => throw Missing();
    }
}