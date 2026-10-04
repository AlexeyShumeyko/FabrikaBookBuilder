using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PhotoBook.Core;
using PhotoBookRenamer.Infrastructure;

namespace PhotoBookRenamer.Application
{
    /// <summary>
    /// Reading the pixel size of photographs the project does not know yet.
    /// </summary>
    /// <remarks>
    /// Two callers need this and neither should have to remember: a project being built from
    /// folders - the card frames need real sizes before anything is shown - and a project
    /// being opened from a file written before sizes were stored. Only the file header is
    /// read, and ImageService caches the answer, so a project saved by this version never
    /// pays for it twice.
    /// </remarks>
    internal static class ProjectImageSizes
    {
        /// <summary>
        /// Fills <see cref="Page.ImageWidth"/> / <see cref="Page.ImageHeight"/> from the files
        /// on disk for pages that do not have them yet, and re-derives each book's card frame.
        /// A project that already knows its sizes is not touched, and not read from disk.
        /// </summary>
        internal static async Task FillMissingAsync(Project? project, IImageService images)
        {
            if (project?.Books == null) return;

            var pending = new List<Page>();
            foreach (var book in project.Books)
            {
                if (book.Cover != null && !book.Cover.HasDimensions) pending.Add(book.Cover);
                foreach (var page in book.Pages)
                    if (page != null && !page.HasDimensions) pending.Add(page);
            }

            if (pending.Count == 0) return;

            await Task.WhenAll(pending.Select(async page =>
            {
                var (width, height) = await images.GetImageDimensionsAsync(page.SourcePath ?? string.Empty);
                page.ImageWidth = width;
                page.ImageHeight = height;
            }));

            foreach (var book in project.Books)
                book.UpdatePageSlots();
        }
    }
}
