using System;
using System.Collections.Generic;
using System.Globalization;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>One project the run will create: its format, its books and its price.</summary>
    /// <remarks>
    /// The row is what the right half of the screen shows beside the books: the format the site
    /// will be asked for, how many books and spreads go into it, and the price the site named for
    /// that set. It carries no decision of its own - everything here was settled by the order
    /// page - so the upload screen cannot show a number the customer did not already see.
    /// </remarks>
    public sealed class UploadGroupRow
    {
        private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

        public UploadGroupRow(
            int number,
            string formatText,
            int spreads,
            decimal price,
            string? currency,
            IReadOnlyList<UploadBookRow> books)
        {
            Number = number;
            FormatText = formatText ?? string.Empty;
            Spreads = spreads;
            Price = price;
            Currency = currency ?? string.Empty;
            Books = books ?? Array.Empty<UploadBookRow>();
        }

        public int Number { get; }

        /// <summary>The format as the site names it, for example «Лайфлат 21×30».</summary>
        public string FormatText { get; }

        /// <summary>Spreads across the project's books, the cover not counted.</summary>
        public int Spreads { get; }

        /// <summary>The price of the whole project, as the site's calculator named it.</summary>
        public decimal Price { get; }

        /// <summary>The currency the site named with the price, or an empty line.</summary>
        public string Currency { get; }

        /// <summary>The books of the project, in the order they go up.</summary>
        public IReadOnlyList<UploadBookRow> Books { get; }

        /// <summary>What the project is called in the list: its number in the run.</summary>
        public string NumberText => "Проект " + Number.ToString(Russian);

        /// <summary>How many books the project holds, in words.</summary>
        public string BooksText => Books.Count == 1
            ? "1 книга"
            : Books.Count.ToString(Russian) + " книг";

        /// <summary>How many spreads the project holds, in words.</summary>
        public string SpreadsText => "Разворотов: " + Spreads.ToString(Russian);

        /// <summary>The price as the site wrote it, or a line saying there is none.</summary>
        public string PriceText => Price <= 0
            ? "цена не названа"
            : Price.ToString("N2", Russian) + (Currency.Length == 0 ? string.Empty : " " + Currency);

        /// <summary>Whether the site named a price for this project.</summary>
        public bool HasPrice => Price > 0;
    }
}
