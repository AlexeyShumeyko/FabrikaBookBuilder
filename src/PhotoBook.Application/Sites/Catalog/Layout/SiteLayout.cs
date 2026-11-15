using System.Collections.Generic;
using System.Globalization;

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
        IReadOnlyList<PartLayout> Parts,
        long IntegrationId = 0)
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
        /// The pixel size a page of this part has to be, for the choices a book actually made.
        /// </summary>
        /// <remarks>
        /// A cover's size depends on which element of an option was picked - a thicker spine for
        /// cardboard - so the book's own selection is what names the index. Trying the selection
        /// keeps the rule in the site's document: the program does not know which option sizes a
        /// page, and does not have to.
        /// </remarks>
        public PixelSize? RequiredPixels(
            long partId,
            int quantity,
            IReadOnlyDictionary<long, long>? elements)
        {
            var part = Part(partId);
            if (part is null)
                return null;

            if (part.FixedSize is not null || elements is null)
                return RequiredPixels(partId, quantity, null, null);

            foreach (var condition in part.SizeIndexes)
            {
                foreach (var chosen in elements)
                {
                    if (!condition.Describes(
                            chosen.Key.ToString(CultureInfo.InvariantCulture),
                            chosen.Value.ToString(CultureInfo.InvariantCulture)))
                        continue;

                    var entry = part.EntryFor(condition.IndexFor(quantity));
                    if (entry is not null)
                        return PageGeometry.Required(entry.Size, entry.Bleeds, ResolutionDpi);
                }
            }

            return RequiredPixels(partId, quantity, null, null);
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
