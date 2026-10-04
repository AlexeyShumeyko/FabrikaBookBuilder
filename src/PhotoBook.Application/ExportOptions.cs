using System;

namespace PhotoBookRenamer.Application
{
    /// <summary>
    /// Options for <see cref="IExportService.ExportProjectAsync(Project, string, ExportOptions)"/>.
    /// </summary>
    public sealed class ExportOptions
    {
        /// <summary>
        /// When true, every book is written into its own subfolder named after the book
        /// (e.g. "001 Иванов"). Defaults to <c>false</c> so the layout stays exactly as
        /// before: one flat folder with 001-00.jpg / 001-01.jpg names, which is what the
        /// client's batch upload system expects.
        /// </summary>
        public bool PerBookSubfolders { get; set; }

        /// <summary>Optional 0..1 progress callback, driven by copied file counts.</summary>
        public IProgress<double>? Progress { get; set; }

        public static ExportOptions Default => new ExportOptions();
    }
}
