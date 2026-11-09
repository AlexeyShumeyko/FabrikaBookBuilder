using PhotoBook.Core;

namespace PhotoBook.Application
{
    /// <summary>
    /// Attaches the reduced copy that a slot or a card should draw.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A page carries the path of its reduced copy so that a binding does not have to work it
    /// out. The path is set only for a copy that is on disk: attaching the path a copy WILL
    /// have was tried and does not work, because a binding is not re-evaluated when the file
    /// appears - the screen opens, builds every frame, and goes on showing empty ones.
    /// </para>
    /// <para>
    /// This runs on a worker thread, from the repository and from the project builder, before
    /// the project is handed to a screen. It used to run in the editors instead, where a
    /// thousand assignments landed on the interface thread on the way in and delayed the
    /// first frame by seconds.
    /// </para>
    /// </remarks>
    public static class PageThumbnails
    {
        public static void Attach(Project project, IThumbnailProvider thumbnails)
        {
            if (project?.Books is null) return;

            foreach (var book in project.Books)
            {
                if (book?.Cover is not null)
                {
                    book.Cover.ThumbnailPath = thumbnails.GetExistingPath(book.Cover.SourcePath ?? string.Empty);
                }

                if (book?.Pages is null) continue;

                foreach (var page in book.Pages)
                {
                    if (page is not null)
                    {
                        page.ThumbnailPath = thumbnails.GetExistingPath(page.SourcePath ?? string.Empty);
                    }
                }
            }
        }
    }
}
