using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace PhotoBookRenamer.Application
{
    /// <summary>
    /// Reduced copies of photographs, for the interface to draw.
    ///
    /// <para>
    /// The program never draws a full-size photograph. The original of a print order is up to
    /// 24 megapixels and the cell it appears in is about 190 pixels; decoding the whole file
    /// to fill that cell is the most expensive thing this product does, and it is why a
    /// project of twenty books took 42 seconds to open. Everything that shows a photograph
    /// therefore shows a reduced copy, and this interface is the only way one is obtained.
    /// </para>
    ///
    /// <para>
    /// It is a port, not a class: the implementation is the platform's. The WPF build decodes
    /// the whole file and shrinks it, which is slow and costs about 96 MB per photograph in
    /// flight. A build on Avalonia asks the engine to read the JPEG already reduced, which is
    /// fast and cheap. The difference is measured, not assumed - see ARCHITECTURE.md.
    /// </para>
    /// </summary>
    public interface IThumbnailProvider
    {
        /// <summary>
        /// The reduced copy if one exists, otherwise null. Answers without touching the
        /// source, so a screen full of cells costs one stat call each and no decoding.
        /// </summary>
        string? GetExistingPath(string sourcePath);

        /// <summary>
        /// The reduced copy, creating it if it does not exist yet. Returns null when the
        /// photograph cannot be read: a missing or damaged file must not stop a project from
        /// opening.
        /// </summary>
        Task<string?> GetOrCreateAsync(string sourcePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Makes sure every photograph in the collection has a reduced copy. Reports progress
        /// by fraction completed and must stop promptly when cancelled.
        /// </summary>
        Task EnsureAsync(
            IReadOnlyCollection<string> sourcePaths,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
