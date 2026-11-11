using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoBook.Application.Sites.Books.Grouping
{
    /// <summary>Lays a project's books out over the site's projects.</summary>
    /// <remarks>
    /// <para>
    /// A group is one project on the site, and a site project holds one product in one format.
    /// So books of two formats cannot share a group whatever the mode says: «в один проект»
    /// means one project per format, not one project for the whole folder. That rule is applied
    /// here rather than left to the upload, which would create a project the site cannot fill.
    /// </para>
    /// <para>
    /// A book may not appear in two groups either: the upload would send it twice, and the site
    /// would accept both copies. The check is by reference, because two books of the same
    /// project can be equal in content and still be two books.
    /// </para>
    /// </remarks>
    public static class BookGrouping
    {
        /// <summary>The layout for a mode that needs no manual assignment.</summary>
        public static BookGroups Split(SiteRegion region, IReadOnlyList<Book> books, GroupingMode mode)
        {
            if (books is null)
                throw new ArgumentNullException(nameof(books));

            if (mode == GroupingMode.Manual)
                throw new ArgumentException("A manual layout needs its groups.", nameof(mode));

            if (books.Count == 0)
                return Empty();

            var groups = mode switch
            {
                GroupingMode.InOneProject => ByFormat(region, books),
                GroupingMode.EachBookSeparate => books
                    .Select(book => new OrderGroup(region, new[] { Present(book) }))
                    .ToList(),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown grouping mode.")
            };

            return new BookGroups(groups, Array.Empty<string>());
        }

        /// <summary>The layout the customer made by hand, checked but not rearranged.</summary>
        public static BookGroups FromManual(
            SiteRegion region,
            IReadOnlyList<IReadOnlyList<Book>> groups)
        {
            if (groups is null)
                throw new ArgumentNullException(nameof(groups));

            var problems = new List<string>();
            var laidOut = new List<OrderGroup>();
            var seen = new HashSet<Book>(ReferenceEqualityComparer.Instance);

            for (var index = 0; index < groups.Count; index++)
            {
                var number = index + 1;
                var books = groups[index];

                if (books is null || books.Count == 0)
                {
                    problems.Add($"Группа {number} пуста.");
                    continue;
                }

                foreach (var book in books)
                {
                    if (!seen.Add(Present(book)))
                        problems.Add($"Книга из группы {number} уже есть в другой группе.");
                }

                var names = books
                    .GroupBy(FormatOf)
                    .Select(format => NameOf(format.First()))
                    .ToList();

                if (names.Count > 1)
                {
                    problems.Add(
                        $"В группе {number} книги разных форматов: {string.Join(" и ", names)}. " +
                        "Один проект на сайте — один формат.");
                }

                laidOut.Add(new OrderGroup(region, books));
            }

            if (laidOut.Count == 0 && problems.Count == 0)
                return Empty();

            return new BookGroups(laidOut, problems);
        }

        /// <summary>One group per format, in the order the formats first appear.</summary>
        private static IReadOnlyList<OrderGroup> ByFormat(SiteRegion region, IReadOnlyList<Book> books)
        {
            var buckets = new List<List<Book>>();
            var positions = new Dictionary<(long Product, long Variant), int>();

            foreach (var book in books)
            {
                var current = Present(book);
                var key = FormatOf(current);

                if (!positions.TryGetValue(key, out var position))
                {
                    position = buckets.Count;
                    positions[key] = position;
                    buckets.Add(new List<Book>());
                }

                buckets[position].Add(current);
            }

            return buckets.Select(bucket => new OrderGroup(region, bucket)).ToList();
        }

        private static BookGroups Empty() =>
            new(Array.Empty<OrderGroup>(), new[] { "В проекте нет ни одной книги." });

        private static Book Present(Book book) => book ?? throw new ArgumentException(
            "The project holds a book that is not there.",
            nameof(book));

        /// <summary>What tells two formats apart on the site: the product and its own format.</summary>
        private static (long Product, long Variant) FormatOf(Book book) =>
            (book.Product.Id, book.Variant.Id);

        /// <summary>The format as the product page names it, for a message to a person.</summary>
        private static string NameOf(Book book) =>
            "«" + book.Product.Title + " " + book.Variant.Title + "»";
    }
}
