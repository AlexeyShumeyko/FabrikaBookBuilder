using System;
using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites.Catalog;
using CoreBook = PhotoBook.Core.Book;
using CoreProject = PhotoBook.Core.Project;

namespace PhotoBook.Application.Sites.Books.Order
{
    /// <summary>
    /// The project turned into the books the site prices and receives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project knows what the customer assembled; the catalog knows what the site sells. This
    /// is the one place the two are joined: a book of the project becomes an order book only when
    /// it has a format chosen and that format is still in the catalog, and everything that cannot
    /// be joined is reported instead of guessed.
    /// </para>
    /// <para>
    /// The part that holds the photographs is the one the site counts in spreads or pages. A
    /// product whose parts cannot be told apart that way is reported: sending the files into the
    /// wrong part would price one book and deliver another.
    /// </para>
    /// </remarks>
    public sealed record OrderDraft(IReadOnlyList<Book> Books, IReadOnlyList<string> Problems)
    {
        /// <summary>Whether every book of the project is ready to be priced and sent.</summary>
        public bool IsComplete => Books.Count > 0 && Problems.Count == 0;

        /// <summary>Builds the order from the project and the catalog, saying what is missing.</summary>
        public static OrderDraft From(
            CoreProject? project,
            IReadOnlyList<SiteProduct>? catalog,
            IFileService files)
        {
            if (project is null)
                return new OrderDraft(Array.Empty<Book>(), new[] { "Проект не открыт." });

            if (project.Books is null || project.Books.Count == 0)
                return new OrderDraft(Array.Empty<Book>(), new[] { "В проекте нет ни одной книги." });

            var products = catalog ?? Array.Empty<SiteProduct>();
            var books = new List<Book>();
            var problems = new List<string>();

            foreach (var source in project.Books)
            {
                var name = NameOf(source);
                var built = Build(source, name, products, files, problems);

                if (built is not null)
                    books.Add(built);
            }

            return new OrderDraft(books, problems);
        }

        private static Book? Build(
            CoreBook source,
            string name,
            IReadOnlyList<SiteProduct> catalog,
            IFileService files,
            List<string> problems)
        {
            if (!source.HasSiteFormat)
            {
                problems.Add(name + ": формат не выбран.");
                return null;
            }

            var product = catalog.FirstOrDefault(item => item.Id == source.SiteProductId);
            if (product is null)
            {
                problems.Add(name + ": выбранного формата нет в каталоге сайта.");
                return null;
            }

            var variant = product.Variants?.FirstOrDefault(item => item.Id == source.SiteVariantId);
            if (variant is null)
            {
                problems.Add(name + ": у формата нет выбранного варианта.");
                return null;
            }

            var pages = PagesOf(source);
            if (pages.Count == 0)
            {
                problems.Add(name + ": ни одной страницы с фотографией.");
                return null;
            }

            var pageParts = (variant.Parts ?? Array.Empty<ProductPart>())
                .Where(part => part.HoldsPages)
                .ToList();

            if (pageParts.Count != 1)
            {
                problems.Add(pageParts.Count == 0
                    ? name + ": у формата не нашлась часть с разворотами."
                    : name + ": у формата несколько частей с разворотами, какая из них — не ясно.");
                return null;
            }

            var pagePart = pageParts[0];
            var range = pagePart.Quantity?.Default;
            if (range is not null && !range.Allows(pages.Count))
            {
                problems.Add(name + ": формат принимает от " + range.From + " до " + range.To +
                    " с шагом " + range.Step + ", а в книге " + pages.Count + ".");
                return null;
            }

            var parts = PartsOf(source, variant, pagePart.Id, pages.Count, out var unanswered);
            if (parts is null)
            {
                problems.Add(name + ": " + unanswered);
                return null;
            }

            var fileList = new List<PageFile>(pages.Count);
            foreach (var page in pages)
            {
                var length = files.FileLength(page.SourcePath!);
                if (length <= 0)
                {
                    problems.Add(name + ": файл не найден — " + page.FileName + ".");
                    return null;
                }

                fileList.Add(new PageFile(page.SourcePath!, length));
            }

            return new Book(product, variant, pagePart.Id, parts, fileList);
        }

        /// <summary>
        /// Every part of the format with what the customer chose, or the reason the book cannot
        /// be priced yet.
        /// </summary>
        /// <remarks>
        /// The site prices a book by its parts and by the element chosen for each parameter, so
        /// a part without its required choice is not a default - it is an unanswered question,
        /// and the site answers it with a zero. The choices are checked against the format's own
        /// elements here, because a choice stored against another format (or an element the
        /// site has since withdrawn) would otherwise reach the site as a combination it does not
        /// sell.
        /// </remarks>
        private static List<SelectedPart>? PartsOf(
            CoreBook source,
            ProductVariant variant,
            long pagePartId,
            int pages,
            out string unanswered)
        {
            unanswered = string.Empty;
            var parts = new List<SelectedPart>();

            foreach (var part in variant.Parts ?? Array.Empty<ProductPart>())
            {
                var chosen = new Dictionary<long, long>();

                // Only the questions the site still asks. An option excluded by an answer
                // already given (the interlayer for cardboard when paper was chosen) is not a
                // missing answer: demanding it made a complete book unpriced.
                var offered = OfferedParameters.Available(
                    part.Options ?? Array.Empty<ProductOption>(),
                    source.SiteOptions);

                foreach (var option in offered)
                {
                    if (!source.SiteOptions.TryGetValue(option.Id, out var elementId))
                    {
                        if (option.IsRequired)
                        {
                            unanswered = "не выбран параметр «" + option.Title + "».";
                            return null;
                        }

                        continue;
                    }

                    if (!option.Elements.Any(element => element.Id == elementId))
                    {
                        unanswered = "выбранный вариант параметра «" + option.Title + "» сайту неизвестен.";
                        return null;
                    }

                    chosen[option.Id] = elementId;
                }

                var quantity = part.Id == pagePartId
                    ? pages
                    : part.Quantity?.Default?.From ?? 0;

                parts.Add(new SelectedPart(part.Id, new PartSelection(chosen, quantity)));
            }

            return parts;
        }

        private static List<PhotoBook.Core.Page> PagesOf(CoreBook book) =>
            (book.Pages ?? new System.Collections.ObjectModel.ObservableCollection<PhotoBook.Core.Page>())
                .Where(page => page is not null && !page.IsCover && !page.IsEmpty)
                .Where(page => !string.IsNullOrWhiteSpace(page.SourcePath))
                .OrderBy(page => page.Index)
                .ToList();

        private static string NameOf(CoreBook book) =>
            string.IsNullOrWhiteSpace(book.Name)
                ? "Книга " + (book.BookIndex + 1)
                : book.Name!.Trim();
    }
}
