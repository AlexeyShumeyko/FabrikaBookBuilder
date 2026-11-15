using System;
using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites.Books;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>What the order page tells the customer about the project it was opened from.</summary>
    /// <remarks>
    /// The sentences are about the project, not about the page: they name the book that is not
    /// full and the photographs the site will not carry, so the customer knows what to fix before
    /// choosing formats and asking for a price.
    /// </remarks>
    internal static class ProjectProblems
    {
        /// <summary>Why the order cannot go, or the note that says what comes next.</summary>
        public static string Of(BookInventory inventory)
        {
            if (inventory is null)
                throw new ArgumentNullException(nameof(inventory));

            var lines = new List<string>();

            foreach (var book in inventory.Books.Where(book => !book.CanUpload))
            {
                lines.Add(book.Name + ": " +
                    (book.Condition == BookCondition.NoPhotos
                        ? "нет ни одной фотографии."
                        : "заполнено " + book.Photos + " из " + book.Slots + " слотов."));
            }

            if (inventory.Books.Count == 0)
                lines.Add("В проекте нет ни одной книги.");

            if (inventory.LeftoverPhotos > 0)
            {
                lines.Add("Фотографий вне книги: " + inventory.LeftoverPhotos +
                    " — они не попадут на сайт.");
            }

            if (lines.Count == 0)
            {
                lines.Add("Выберите формат для каждой книги — цена считается по нему.");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
