using System;
using System.Collections.Generic;
using System.Linq;

namespace PhotoBook.Application.Sites.Books.Files
{
    /// <summary>Checks the books of one order against each other.</summary>
    /// <remarks>
    /// A group is one order on the site, so its books have to be the same book: pages of
    /// different pixel sizes mean the order holds two formats, which the site will print as two
    /// products under one project. The customer decides what to do - move the book into a group
    /// of its own, or correct the format and its parameters - but the order cannot go while it
    /// holds both, and the sentence says which groups the sizes belong to.
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
                    FileAlertLevel.Blocking,
                    null,
                    $"Книги в заказе требуют разный размер страницы: {described}. " +
                    "Переместите книгу в другую группу или проверьте выбранный формат и параметры.")
            };
        }
    }
}
