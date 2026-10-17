using System.Collections.Generic;
using System.Linq;
using System.IO;
using PhotoBook.Core;

namespace PhotoBook.Application
{
    /// <summary>
    /// What a card in the project list needs, worked out from the project itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One place, on purpose. The book count, the spread count, the total photographs and the
    /// three preview paths are four things about a project that have to agree with each
    /// other, and there were four places that worked them out separately - one of them a
    /// view model, which is the wrong layer for a rule about what a project is.
    /// </para>
    /// <para>
    /// It lives here so that whoever saves a project and whoever draws a card are asking
    /// the same question the same way.
    /// </para>
    /// </remarks>
    public static class ProjectCardData
    {
        /// <summary>
        /// How many photographs a card shows before the "+N" tile.
        /// </summary>
        public const int PreviewLimit = 3;

        /// <summary>
        /// Fills the numbers and the preview strip on an index entry from the project.
        /// </summary>
        /// <remarks>
        /// Only paths that still exist are taken. A project can outlive a photograph that was
        /// moved or deleted, and a broken image in the strip is worse than one tile fewer.
        /// </remarks>
        public static void Fill(Project? project, ProjectInfo entry)
        {
            if (project is null || entry is null) return;

            entry.BookCount = project.Books?.Count ?? 0;

            var first = project.Books is { Count: > 0 } ? project.Books[0] : null;
            entry.PageCount = (first?.Pages is null ? 0 : Enumerable.Count(first.Pages, p => !p.IsCover));
            entry.Status = StatusOf(project);

            FillPreviews(project, entry);
        }

        /// <summary>
        /// How far along a run is, from the run itself.
        /// </summary>
        /// <remarks>
        /// "Every slot filled" is the rule, not "the last save said so": a run that has been
        /// emptied of photographs by the export is not ready, and one that has been filled
        /// since the index was written is.
        /// </remarks>
        public static ProjectStatus StatusOf(Project project)
        {
            var books = project.Books;
            if (books is null || books.Count == 0) return ProjectStatus.NotFilled;

            foreach (var book in books)
            {
                if (book?.Pages is null) return ProjectStatus.NotFilled;

                foreach (var page in book.Pages)
                {
                    if (page is null) return ProjectStatus.NotFilled;
                    if (string.IsNullOrEmpty(page.SourcePath)) return ProjectStatus.NotFilled;
                }
            }

            return ProjectStatus.Ready;
        }

        private static void FillPreviews(Project project, ProjectInfo entry)
        {
            entry.PreviewPhotos.Clear();
            entry.TotalPhotoCount = 0;

            var books = project.Books;
            if (books is null) return;

            foreach (var book in books)
            {
                if (book is null) continue;

                // Covers first, then spreads, book by book: that is the order the photographs
                // appear in the finished book, and the card is a preview of the book.
                if (book.Cover is not null)
                {
                    AddIfUsable(book.Cover.SourcePath, entry);
                }

                var pages = book.Pages;
                if (pages is null) continue;
                foreach (var page in pages)
                {
                    AddIfUsable(page?.SourcePath, entry);
                }
            }
        }

        private static void AddIfUsable(string? path, ProjectInfo entry)
        {
            if (string.IsNullOrEmpty(path)) return;

            entry.TotalPhotoCount++;

            if (entry.PreviewPhotos.Count >= PreviewLimit) return;
            if (!File.Exists(path)) return;

            entry.PreviewPhotos.Add(path);
        }
    }
}
