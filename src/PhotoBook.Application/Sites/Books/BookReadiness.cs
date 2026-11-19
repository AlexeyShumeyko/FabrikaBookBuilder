using System;
using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites.Catalog;

namespace PhotoBook.Application.Sites.Books
{
    /// <summary>Why a book cannot be sent to the site yet.</summary>
    public enum BookProblemKind
    {
        /// <summary>The customer has not added a single photo.</summary>
        NoFiles,

        /// <summary>Some pages of the chosen book still have no photo.</summary>
        MissingPages,

        /// <summary>There are more files than the chosen book has pages.</summary>
        ExtraFiles,

        /// <summary>A part is chosen in a way the site does not price.</summary>
        InvalidSelection,

        /// <summary>The site did not answer with a price.</summary>
        NoPrice,

        /// <summary>The site answered with a price of zero, which means the set is not sellable.</summary>
        ZeroPrice
    }

    /// <summary>One reason a book cannot be sent, in a form the window can show.</summary>
    public sealed record BookProblem(BookProblemKind Kind, string Message);

    /// <summary>
    /// The single gate between a book the customer is working on and a book sent to the site.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A book that leaves the program wrong is the program's fault, not the customer's, and the
    /// customer will not report it: an order that is refused, or one the site prices at zero,
    /// costs the business money and is never fixed. So every check lives here and is asked once
    /// before the first request of an order, rather than being spread over the upload and the
    /// checkout where one of them can be forgotten.
    /// </para>
    /// <para>
    /// The price is part of readiness and not a display detail: <c>0</c> is how the price
    /// endpoint says "this set is not sellable", so a zero price blocks the book the same way a
    /// missing page does. Nothing may be sent with a price that was not read from the site.
    /// </para>
    /// </remarks>
    public sealed record BookReadiness(bool IsReady, IReadOnlyList<BookProblem> Problems)
    {
        public static BookReadiness Ready() => new(true, Array.Empty<BookProblem>());

        public static BookReadiness Blocked(IReadOnlyList<BookProblem> problems) =>
            new(false, problems);

        /// <summary>
        /// Checks one book against its selections and the price the site returned for them.
        /// </summary>
        /// <param name="book">The book to send.</param>
        /// <param name="selections">The verdict on each part choice, in any order.</param>
        /// <param name="price">The price the site answered with, or null when there is none.</param>
        public static BookReadiness Check(
            Book book,
            IReadOnlyList<PartCheck> selections,
            decimal? price)
        {
            if (book is null)
                throw new ArgumentNullException(nameof(book));

            var problems = new List<BookProblem>();

            foreach (var check in selections ?? Array.Empty<PartCheck>())
            {
                if (check is null || check.IsValid)
                    continue;

                problems.AddRange(check.Problems.Select(problem =>
                    new BookProblem(BookProblemKind.InvalidSelection, problem.Message)));
            }

            if (book.FileCount == 0)
            {
                problems.Add(new BookProblem(
                    BookProblemKind.NoFiles,
                    "В книге нет ни одной фотографии."));
            }
            else if (book.MissingPages > 0)
            {
                problems.Add(new BookProblem(
                    BookProblemKind.MissingPages,
                    $"Не хватает фотографий: {book.MissingPages} из {book.PageSlots} страниц пусты."));
            }
            else if (book.ExtraFiles > 0)
            {
                problems.Add(new BookProblem(
                    BookProblemKind.ExtraFiles,
                    $"Лишних файлов: {book.ExtraFiles}. В книге {book.PageSlots} страниц."));
            }

            if (price is null)
            {
                problems.Add(new BookProblem(
                    BookProblemKind.NoPrice,
                    "Цена книги не получена — отправлять её нельзя."));
            }
            else if (price <= 0)
            {
                problems.Add(new BookProblem(
                    BookProblemKind.ZeroPrice,
                    "Сайт оценил книгу в 0 — такой набор он не продаёт."));
            }

            return problems.Count == 0 ? Ready() : Blocked(problems);
        }
    }
}
