using System.Collections.Generic;

namespace PhotoBook.Application.Sites.Catalog.Layout
{
    /// <summary>
    /// The geometry of one product and format, as the uploader endpoint describes it.
    /// </summary>
    /// <remarks>
    /// The resolution is part of the document rather than a constant in the program: the site
    /// prints at 300 dpi today and says so here, and a product that prints at another
    /// resolution would otherwise be checked against the wrong number with nothing failing.
    /// </remarks>
    public sealed record SiteLayout(
        long ProductId,
        long ComponentId,
        int ResolutionDpi,
        IReadOnlyList<PartLayout> Parts)
    {
        public PartLayout? Part(long partId)
        {
            for (var index = 0; index < Parts.Count; index++)
            {
                if (Parts[index].PartId == partId)
                    return Parts[index];
            }

            return null;
        }

        /// <summary>
        /// The pixel size a file has to be for one part at one quantity, or null when the site
        /// did not say - which the caller shows rather than replaces with a guessed size.
        /// </summary>
        public PixelSize? RequiredPixels(long partId, int spreads, string? optionId, string? elementId)
        {
            var part = Part(partId);
            if (part is null)
                return null;

            var index = part.FixedSize is null ? part.IndexFor(spreads, optionId, elementId) : null;
            var entry = part.EntryFor(index);

            return entry is null ? null : PageGeometry.Required(entry.Size, entry.Bleeds, ResolutionDpi);
        }
    }
}
