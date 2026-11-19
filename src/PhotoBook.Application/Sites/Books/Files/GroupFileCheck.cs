using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoBook.Application.Sites.Books.Files
{
    /// <summary>Checks the books of one order against each other.</summary>
    /// <remarks>
    /// A group is what the customer confirms as one order, so its books have to be the same
    /// book: pages of different pixel sizes mean the order holds two formats, which is either a
    /// deliberate mix or a book left in the wrong group. Only the customer can tell those
    /// apart, so this asks rather than decides.
    /// </remarks>
    public static class GroupFileCheck
    {
        /// <summary>One alert when the books do not all need the same page size.</summary>
        /// <param name="rules">What each book of the group asks of a page, in group order.</param>
        public static IReadOnlyList<FileAlert> Check(IReadOnlyList<PageImageRule> rules)
        {
            if (rules is null)
                throw new ArgumentNullException(nameof(rules));

            var books = rules.Where(rule => rule is not null).ToList();
            if (books.Count < 2)
                return Array.Empty<FileAlert>();

            var sizes = books
                .GroupBy(book => book.Required)
                .Select(group => (Size: group.Key, Count: group.Count()))
                .ToList();

            if (sizes.Count < 2)
                return Array.Empty<FileAlert>();

            var described = string.Join(
                " и ",
                sizes.Select(size => $"{size.Size} ({size.Count} кн.)"));

            return new[]
            {
                new FileAlert(
                    FileAlertLevel.NeedsConfirmation,
                    null,
                    $"Книги в заказе требуют разный размер страницы: {described}. " +
                    "Сайт напечатает их как разные книги — проверьте, так ли задумано.")
            };
        }
    }
}
