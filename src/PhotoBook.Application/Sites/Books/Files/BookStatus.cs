using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoBook.Application.Sites.Books.Files
{
    /// <summary>What stands between one book and the site, in a word.</summary>
    public enum BookStatusKind
    {
        /// <summary>The files are being read, or have not been read yet.</summary>
        Checking,

        /// <summary>Every file matches what the site asks, and the book is full.</summary>
        Ready,

        /// <summary>The site would refuse a file: wrong size, wrong shape, or too many.</summary>
        FilesFailed,

        /// <summary>The chosen format has a cover, and the book has no cover file.</summary>
        NoCover,

        /// <summary>Fewer spreads than the book's page part takes.</summary>
        MissingSpreads
    }

    /// <summary>One book as the list shows it: a word, and the reasons behind the word.</summary>
    /// <remarks>
    /// <para>
    /// The order of the checks is the order a person would take them in: a book with no cover is
    /// not worth measuring page by page, and a book that is short of pages cannot be sent
    /// whatever its files look like. So the first thing found is the status, and the rest is kept
    /// as reasons for the book's own expansion rather than lost.
    /// </para>
    /// <para>
    /// A file the site may trim is not a failure: the customer says yes to it on the screen, and
    /// the book stays «Готова». Only a file the site would refuse stops the book.
    /// </para>
    /// <para>
    /// What the site said about one file is not repeated here. The expansion under the book
    /// already lists every file with its own complaint, and a second copy above that list - all
    /// of it in red, naming pages the customer reads as spreads - was the same text twice. The
    /// reasons here are the ones about the book itself: no cover, or fewer spreads than its part
    /// takes.
    /// </para>
    /// </remarks>
    public sealed record BookStatus(BookStatusKind Kind, string Text, IReadOnlyList<string> Reasons)
    {
        /// <summary>Whether nothing stands in the way of this book.</summary>
        public bool IsReady => Kind == BookStatusKind.Ready;

        /// <summary>Whether the book has something that keeps it off the upload.</summary>
        public bool HasProblem => Kind is BookStatusKind.FilesFailed or BookStatusKind.NoCover
            or BookStatusKind.MissingSpreads;

        /// <summary>The book's status, from the book itself and the check that looked at its files.</summary>
        /// <param name="book">The book as it would be sent.</param>
        /// <param name="check">What the check said about this book, or null before it ran.</param>
        /// <param name="isChecking">Whether the files are being read right now.</param>
        public static BookStatus Of(Book book, CheckedBook? check, bool isChecking)
        {
            if (book is null)
                throw new ArgumentNullException(nameof(book));

            if (NeedsCover(book) && book.CoverFile is null)
            {
                return new BookStatus(
                    BookStatusKind.NoCover,
                    "Нет обложки",
                    new[] { "У формата есть обложка, а файла обложки в книге нет." });
            }

            if (book.MissingPages > 0)
            {
                return new BookStatus(
                    BookStatusKind.MissingSpreads,
                    "Не хватает разворотов",
                    new[] { $"Не хватает разворотов: {book.MissingPages} из {book.PageSlots} пусты." });
            }

            var alerts = check?.Alerts ?? Array.Empty<FileAlert>();

            if (alerts.Any(alert => alert.Level == FileAlertLevel.Blocking))
            {
                return new BookStatus(
                    BookStatusKind.FilesFailed,
                    "Ошибка в файлах",
                    Array.Empty<string>());
            }

            if (check is null || isChecking)
                return new BookStatus(BookStatusKind.Checking, "Проверяется", Array.Empty<string>());

            return new BookStatus(BookStatusKind.Ready, "Готова", Array.Empty<string>());
        }

        /// <summary>Whether the chosen format asks for a cover at all.</summary>
        private static bool NeedsCover(Book book) => book.CoverPartId > 0;
    }
}
