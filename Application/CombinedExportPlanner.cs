using System;
using System.Collections.Generic;
using System.Linq;
using PhotoBookRenamer.Domain;

namespace PhotoBookRenamer.Application
{
    /// <summary>
    /// One file the combined run has to produce.
    /// </summary>
    public sealed class ExportPlanEntry
    {
        /// <summary>File to copy from.</summary>
        public string SourcePath { get; init; } = string.Empty;

        /// <summary>Name to write, in the KKK-FF.jpg form the client's site accepts.</summary>
        public string FileName { get; init; } = string.Empty;

        /// <summary>
        /// 0 for a file that applies to EVERY book of the run, otherwise the one book it
        /// belongs to. The site reads a 000 index as "this is the run-wide default for
        /// this position".
        /// </summary>
        public int BookIndex { get; init; }

        /// <summary>0 for the cover, 1..N for the spreads. The position, not a running count.</summary>
        public int SlotIndex { get; init; }

        /// <summary>How many books this file covers.</summary>
        public int BookCount { get; init; }

        /// <summary>
        /// True for the extra file that exists only to tell the site how many books the
        /// run has. See <see cref="CombinedExportPlanner"/> for why that is needed.
        /// </summary>
        public bool CarriesBookCount { get; init; }
    }

    /// <summary>What a combined run will produce, before anything is copied.</summary>
    public sealed class ExportPlan
    {
        public IReadOnlyList<ExportPlanEntry> Entries { get; init; } = Array.Empty<ExportPlanEntry>();

        public int BookCount { get; init; }

        public int SharedCount => Entries.Count(e => e.BookIndex == 0);

        public int PerBookCount => Entries.Count - SharedCount;

        /// <summary>
        /// True when at least one file carries the last book's index, so the site can work
        /// out the size of the run on its own.
        /// </summary>
        public bool BookCountCarried { get; init; }

        /// <summary>Set when the run's size cannot be conveyed - the last book has no photos.</summary>
        public string? Warning { get; init; }
    }

    /// <summary>
    /// Works out the smallest correct file set for a combined run.
    ///
    /// The client's site takes KKK-FF.jpg, where FF is a position and the book index has a
    /// special meaning: 000 is the run-wide default for that position, and any KKK file
    /// overrides it for that one book. So a run of five books with a shared cover, three
    /// shared spreads and one personal spread is six files, not twenty:
    ///
    ///   000-00.jpg   the cover, for every book
    ///   000-01.jpg   shared spread 1, for every book
    ///   000-02.jpg   shared spread 2, for every book
    ///   000-03.jpg   shared spread 3, for every book
    ///   001-03.jpg   book 1's own spread 3 - it overrides the default for book 1
    ///   002-03.jpg   book 2's own
    ///   003-03.jpg   book 3's own
    ///   ...
    ///
    /// The site learns the run's size from the HIGHEST book index it can see. That is why
    /// the override files matter: 005-03 is not just a file, it is also the statement
    /// "this run has five books". A run where every single position is shared has no such
    /// file anywhere, and then the size would be lost - so one extra file is written with
    /// the last book's index on the first position that has a photo, holding the same
    /// picture. Nothing changes for the book, and the run's size arrives with it.
    ///
    /// At every position the books are grouped by which photo they hold. The group holding
    /// the most books becomes the 000 default, ties going to the group that owns the lowest
    /// book number, and only if that group is at least two books - a "shared" file used by a
    /// single book would just duplicate that book's own file. Every other group is written
    /// once per book, as an override.
    /// </summary>
    public static class CombinedExportPlanner
    {
        /// <summary>Book index that means "every book of the run".</summary>
        public const int SharedBookIndex = 0;

        public static ExportPlan Build(Project? project, Func<int, int, string> fileName)
        {
            if (project == null) return new ExportPlan();

            var books = project.Books
                .Where(b => b != null)
                .OrderBy(b => b.BookIndex)
                .ToList();

            if (books.Count == 0) return new ExportPlan();

            int bookCount = books.Count;
            int spreadCount = books.Max(b => b.Pages.Count(p => p != null && !p.IsCover));
            var entries = new List<ExportPlanEntry>();

            for (int slot = 0; slot <= spreadCount; slot++)
            {
                var groups = GroupByPhoto(books, slot);
                if (groups.Count == 0) continue;

                var shared = groups
                    .Where(g => g.Books.Count >= 2)
                    .OrderByDescending(g => g.Books.Count)
                    .ThenBy(g => g.Books.Min(b => b.BookIndex))
                    .FirstOrDefault();

                if (shared != null)
                {
                    entries.Add(new ExportPlanEntry
                    {
                        SourcePath = shared.PhotoPath,
                        FileName = fileName(SharedBookIndex, slot),
                        BookIndex = SharedBookIndex,
                        SlotIndex = slot,
                        BookCount = shared.Books.Count
                    });

                    foreach (var group in groups.Where(g => g != shared))
                    {
                        foreach (var book in group.Books)
                        {
                            entries.Add(Override(group.PhotoPath, book, slot, bookCount, fileName));
                        }
                    }
                }
                else
                {
                    // Nobody shares this position: one file per book, no default.
                    foreach (var group in groups)
                    {
                        foreach (var book in group.Books)
                        {
                            entries.Add(Override(group.PhotoPath, book, slot, bookCount, fileName));
                        }
                    }
                }
            }

            // The size of the run only reaches the site through the highest book index in
            // the set. If nothing carries the last book, say so with one extra file.
            string? warning = null;
            bool carried = entries.Any(e => e.BookIndex >= bookCount);

            if (!carried && bookCount > 1)
            {
                var last = books[^1];
                var carrySlot = FirstFilledSlot(last);
                if (carrySlot is int slot)
                {
                    entries.Add(new ExportPlanEntry
                    {
                        SourcePath = SourceAt(last, slot)!,
                        FileName = fileName(last.BookIndex, slot),
                        BookIndex = last.BookIndex,
                        SlotIndex = slot,
                        BookCount = 1,
                        CarriesBookCount = true
                    });
                    carried = true;
                }
                else
                {
                    warning =
                        "Последняя книга пустая, поэтому передать количество книг нечем: " +
                        "в наборе не будет файла с её номером.";
                }
            }

            return new ExportPlan
            {
                Entries = entries,
                BookCount = bookCount,
                BookCountCarried = carried,
                Warning = warning
            };
        }

        private static ExportPlanEntry Override(string photoPath, Book book, int slot, int bookCount,
            Func<int, int, string> fileName) => new()
            {
                SourcePath = photoPath,
                FileName = fileName(book.BookIndex, slot),
                BookIndex = book.BookIndex,
                SlotIndex = slot,
                BookCount = bookCount
            };

        private sealed class Group
        {
            public string PhotoPath { get; init; } = string.Empty;
            public List<Book> Books { get; } = new();
        }

        /// <summary>
        /// Books holding a photo at this position, grouped by that photo. Paths are
        /// compared case-insensitively, the way Windows treats them.
        /// </summary>
        private static List<Group> GroupByPhoto(List<Book> books, int slot)
        {
            var groups = new List<Group>();

            foreach (var book in books)
            {
                var path = SourceAt(book, slot);
                if (string.IsNullOrEmpty(path)) continue;

                var existing = groups.FirstOrDefault(g =>
                    string.Equals(g.PhotoPath, path, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    existing = new Group { PhotoPath = path };
                    groups.Add(existing);
                }

                existing.Books.Add(book);
            }

            return groups;
        }

        private static int? FirstFilledSlot(Book book)
        {
            if (book.Cover != null && !book.Cover.IsEmpty && !string.IsNullOrEmpty(book.Cover.SourcePath))
                return 0;

            foreach (var page in book.Pages.Where(p => p != null && !p.IsCover).OrderBy(p => p.Index))
            {
                if (!page.IsEmpty && !string.IsNullOrEmpty(page.SourcePath))
                    return page.Index;
            }

            return null;
        }

        /// <summary>
        /// The photo a book holds at a position. 0 is the cover, n is the page whose own
        /// index is n - the position, NOT a running count, so a gap in the middle of a book
        /// does not shift every later spread one slot to the left.
        /// </summary>
        private static string? SourceAt(Book book, int slot)
        {
            if (slot == 0)
                return book.Cover != null && !book.Cover.IsEmpty ? book.Cover.SourcePath : null;

            var page = book.Pages.FirstOrDefault(p => p != null && !p.IsCover && p.Index == slot);
            return page != null && !page.IsEmpty ? page.SourcePath : null;
        }
    }
}
