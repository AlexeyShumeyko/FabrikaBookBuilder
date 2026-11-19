using System;
using System.Collections.Generic;
using System.Linq;
using CoreBook = PhotoBook.Core.Book;

namespace PhotoBook.Application.Sites.Books.Grouping
{
    /// <summary>
    /// Splits a project's books into the sets that would become separate projects on the site.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A site project holds one format, so a program project holding two formats cannot go as
    /// one order. The page has to know that before anybody chooses anything, and the honest
    /// evidence is the books themselves: a book's spreads are exported at one pixel size, and two
    /// books exported at different sizes are two formats as far as the site is concerned. That is
    /// what can be read without asking the site and without guessing a millimetre size.
    /// </para>
    /// <para>
    /// The format stored on a book cannot be the evidence. A project assembled from two folders
    /// keeps one stored format on both books - the customer picked it once for the whole project -
    /// and its two sizes disagree with it. So the size leads and the stored format only joins in
    /// where a book has no pages to measure.
    /// </para>
    /// <para>
    /// The slack is deliberate: two exports of one format are the same size to the pixel, but a
    /// sequence written by another program can round one spread by a pixel or two, and a page
    /// that answered that with two groups would ask the customer to choose the same format twice.
    /// A percent is well under the difference between any two formats the site sells.
    /// </para>
    /// </remarks>
    public static class BookSplitting
    {
        /// <summary>How far two page sizes may differ and still be the same format.</summary>
        public const double Slack = 0.01;

        /// <summary>
        /// The project's books, in the order they came in, laid out over the sets that go to the
        /// site as separate projects.
        /// </summary>
        /// <returns>
        /// One set per format, in the order the formats first appear. The sets together hold every
        /// book of the project, and no book appears twice.
        /// </returns>
        public static IReadOnlyList<IReadOnlyList<CoreBook>> Split(IReadOnlyList<CoreBook>? books)
        {
            var sets = new List<List<CoreBook>>();
            var sizes = new List<(int Width, int Height)?>();
            var formats = new List<HashSet<(long Product, long Variant)>>();

            foreach (var book in books ?? Array.Empty<CoreBook>())
            {
                if (book is null)
                    continue;

                var size = SizeOf(book);
                var format = FormatOf(book);

                var index = size is { } page
                    ? IndexOfSize(sizes, page)
                    : IndexOfFormat(sets, formats, format);

                if (index < 0)
                {
                    index = sets.Count;
                    sets.Add(new List<CoreBook>());
                    sizes.Add(size);
                    formats.Add(new HashSet<(long, long)>());
                }

                sets[index].Add(book);

                if (sizes[index] is null && size is not null)
                    sizes[index] = size;

                if (format is { } pair)
                    formats[index].Add(pair);
            }

            return sets;
        }

        /// <summary>
        /// The page size a book's spreads were exported at, or null when it has no page to read.
        /// </summary>
        /// <remarks>
        /// The first spread, not every one of them: a book is exported as one format, and a book
        /// whose spreads disagree with each other is the file check's business, not the way the
        /// groups are laid out.
        /// </remarks>
        public static (int Width, int Height)? SizeOf(CoreBook book)
        {
            if (book?.Pages is null)
                return null;

            foreach (var page in book.Pages)
            {
                if (page is null || page.IsCover || page.IsEmpty)
                    continue;

                if (page.ImageWidth > 0 && page.ImageHeight > 0)
                    return (page.ImageWidth, page.ImageHeight);
            }

            return null;
        }

        /// <summary>Whether two page sizes are close enough to be one format.</summary>
        private static bool Same((int Width, int Height) first, (int Width, int Height) second) =>
            Near(first.Width, second.Width) && Near(first.Height, second.Height);

        private static bool Near(int first, int second)
        {
            var larger = Math.Max(Math.Abs(first), Math.Abs(second));
            return larger == 0 || Math.Abs(first - second) <= larger * Slack;
        }

        private static int IndexOfSize(
            List<(int Width, int Height)?> sizes,
            (int Width, int Height) size)
        {
            for (var index = 0; index < sizes.Count; index++)
            {
                if (sizes[index] is { } known && Same(known, size))
                    return index;
            }

            return -1;
        }

        /// <summary>
        /// The set a book without pages belongs to: the one already holding its stored format, or
        /// the first one, because a book with nothing to tell it apart belongs with the rest rather
        /// than in a group of its own that asks the same questions again.
        /// </summary>
        private static int IndexOfFormat(
            List<List<CoreBook>> sets,
            List<HashSet<(long Product, long Variant)>> formats,
            (long Product, long Variant)? format)
        {
            if (format is { } pair)
            {
                for (var index = 0; index < formats.Count; index++)
                {
                    if (formats[index].Contains(pair))
                        return index;
                }
            }

            return sets.Count > 0 ? 0 : -1;
        }

        private static (long Product, long Variant)? FormatOf(CoreBook book) =>
            book.HasSiteFormat ? (book.SiteProductId!.Value, book.SiteVariantId!.Value) : null;
    }
}
