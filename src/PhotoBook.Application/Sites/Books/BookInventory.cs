using System;
using System.Collections.Generic;
using System.Linq;
using CoreBook = PhotoBook.Core.Book;
using CorePage = PhotoBook.Core.Page;
using CoreProject = PhotoBook.Core.Project;

namespace PhotoBook.Application.Sites.Books
{
    /// <summary>What a book looks like before anybody sends it anywhere.</summary>
    public enum BookCondition
    {
        /// <summary>Every slot holds a photo.</summary>
        Ready,

        /// <summary>Not one slot holds a photo: the book exists as a frame only.</summary>
        NoPhotos,

        /// <summary>Some slots hold a photo, others are still empty.</summary>
        Incomplete
    }

    /// <summary>
    /// One book of the project as the preparation list shows it.
    /// </summary>
    /// <param name="Name">The book's name, or its number when it has none.</param>
    /// <param name="Number">The book's number in the project, starting at one.</param>
    /// <param name="Spreads">Spreads in the book, the cover not counted.</param>
    /// <param name="Photos">Slots holding a photo, the cover counted.</param>
    /// <param name="HasCover">Whether the cover slot holds a photo.</param>
    /// <param name="Condition">What still has to happen before the book can go.</param>
    /// <remarks>
    /// The numbers are read from the project every time rather than stored, because the project
    /// is what the customer edits: a list that cached them would show yesterday's book after a
    /// photo was dropped in.
    /// </remarks>
    public sealed record PreparedBook(
        string Name,
        int Number,
        int Spreads,
        int Photos,
        bool HasCover,
        BookCondition Condition)
    {
        /// <summary>Slots in the book, the cover counted.</summary>
        public int Slots => Spreads + 1;

        /// <summary>Whether the book may be sent; only a full book may.</summary>
        public bool CanUpload => Condition == BookCondition.Ready;
    }

    /// <summary>
    /// The books of a project, as they stand, plus the photographs the project never placed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The list keeps the project's order and gives every book a name: a book called «Книга 12»
    /// in a message is easier to find than "one of the books". A book with no photos is listed
    /// rather than hidden - it is exactly the book the customer needs to see before pressing
    /// anything.
    /// </para>
    /// <para>
    /// Leftover photographs are counted, not attached to a book: a photograph that was never
    /// placed belongs to the folder, and guessing which book it was meant for would be a guess.
    /// The number is what the preparation screen can warn about.
    /// </para>
    /// </remarks>
    public sealed record BookInventory(IReadOnlyList<PreparedBook> Books, int LeftoverPhotos)
    {
        /// <summary>Whether there is at least one book, and every one of them is full.</summary>
        public bool AllReady => Books.Count > 0 && Books.All(book => book.CanUpload);

        /// <summary>Reads the project's books and the photographs it never placed.</summary>
        public static BookInventory From(CoreProject project)
        {
            if (project is null)
                throw new ArgumentNullException(nameof(project));

            var books = project.Books
                .Select((book, index) => Describe(book, index + 1))
                .ToList();

            return new BookInventory(books, CountLeftovers(project));
        }

        private static PreparedBook Describe(CoreBook book, int number)
        {
            var spreads = book.Pages?.Count(page => page is not null && !page.IsCover) ?? 0;
            var photos = book.FilledSlotCount;
            var hasCover = book.Cover is { IsEmpty: false };
            var name = string.IsNullOrWhiteSpace(book.Name) ? $"Книга {number}" : book.Name!.Trim();

            var condition = photos == 0
                ? BookCondition.NoPhotos
                : photos < spreads + 1
                    ? BookCondition.Incomplete
                    : BookCondition.Ready;

            return new PreparedBook(name, number, spreads, photos, hasCover, condition);
        }

        /// <summary>
        /// Photographs the project holds but no book slot uses.
        /// </summary>
        /// <remarks>
        /// Paths are compared the way the project stores them, without asking the disk: a photo
        /// that was moved after it was placed is still placed, and the file check belongs to a
        /// later step that actually reads the files.
        /// </remarks>
        private static int CountLeftovers(CoreProject project)
        {
            if (project.AvailableFiles is null || project.AvailableFiles.Count == 0)
                return 0;

            var slots = project.Books
                .Select(book => book.Cover)
                .Concat(project.Books.SelectMany(book => book.Pages ?? Enumerable.Empty<CorePage>()));

            var placed = new HashSet<string>(
                slots
                    .Where(page => page is not null && !page.IsEmpty)
                    .Select(page => page!.SourcePath!),
                StringComparer.OrdinalIgnoreCase);

            return project.AvailableFiles
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(path => !placed.Contains(path!));
        }
    }
}
