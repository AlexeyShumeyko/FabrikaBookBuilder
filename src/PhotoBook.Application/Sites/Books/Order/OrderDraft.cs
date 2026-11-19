using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PhotoBook.Application.Sites.Catalog;
using CoreBook = PhotoBook.Core.Book;
using CoreProject = PhotoBook.Core.Project;

namespace PhotoBook.Application.Sites.Books.Order
{
    /// <summary>One book the draft refused, and the sentence that says why.</summary>
    /// <param name="Book">The project's own book, so the page can find its row.</param>
    /// <param name="Message">The full sentence, starting with the book's name.</param>
    public sealed record BookDraftProblem(CoreBook Book, string Message);

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
    public sealed record OrderDraft(
        IReadOnlyList<Book> Books,
        IReadOnlyList<string> Problems,
        IReadOnlyList<BookDraftProblem>? BookProblems = null)
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

            return From(project.Books, catalog, files);
        }

        /// <summary>
        /// Builds the order from a set of the project's books, saying what is missing.
        /// </summary>
        /// <remarks>
        /// A set rather than the whole project, because a project may hold books of two formats
        /// and the site takes one format per project: the page prices and sends each such set on
        /// its own, and the assembly has to follow the same lines the customer sees.
        /// </remarks>
        public static OrderDraft From(
            IReadOnlyList<CoreBook>? books,
            IReadOnlyList<SiteProduct>? catalog,
            IFileService files)
        {
            if (books is null || books.Count == 0)
                return new OrderDraft(Array.Empty<Book>(), new[] { "В проекте нет ни одной книги." });

            var products = catalog ?? Array.Empty<SiteProduct>();
            var built = new List<Book>();
            var problems = new List<string>();
            var placed = new List<BookDraftProblem>();

            foreach (var source in books)
            {
                var name = NameOf(source);
                var before = problems.Count;
                var book = Build(source, name, products, files, problems);

                if (book is not null)
                {
                    built.Add(book);
                    continue;
                }

                // The page shows a book's own complaint on the book's row, so the draft keeps the
                // book it belongs to rather than leaving the page to match a name in a sentence.
                if (problems.Count > before)
                    placed.Add(new BookDraftProblem(source, problems[problems.Count - 1]));
            }

            return new OrderDraft(built, problems, placed);
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
                    ? name + ": сайт не описал, куда отправлять развороты этого формата — выберите другой формат."
                    : name + ": у формата несколько частей с разворотами — программа не знает, куда отправлять файлы.");
                return null;
            }

            var pagePart = pageParts[0];

            var parts = PartsOf(source, variant, pagePart.Id, pages.Count, out var unanswered);
            if (parts is null)
            {
                problems.Add(name + ": " + unanswered);
                return null;
            }

            // The range is read from the answers the format still asks for, not from everything
            // the book keeps: an answer to a parameter that stopped applying - the paper
            // interlayer after cardboard was chosen - would otherwise keep its old range and
            // refuse the book for the parameters the customer has just left.
            var quantity = pagePart.Quantity;
            var range = quantity?.RangeFor(SelectionOf(parts, pagePart.Id));
            if (range is not null && !range.Allows(pages.Count))
            {
                problems.Add(OutOfRange(name, quantity!, range, pages.Count));
                return null;
            }

            if (!TryCoverPartOf(variant, name, problems, out var coverPart))
                return null;

            // Every missing file is named at once. The check used to stop at the first one, so a
            // customer who had deleted the folder learned about the cover, replaced it, and only
            // then heard about the first spread - one file per attempt, for a book whose whole
            // source is gone. The book still cannot go, but the sentence lists everything it lacks.
            var missing = new List<string>();
            var coverFile = default(PageFile);
            if (coverPart is not null)
            {
                var cover = source.Cover;
                if (cover is null || cover.IsEmpty || string.IsNullOrWhiteSpace(cover.SourcePath))
                {
                    missing.Add("обложка (не назначена)");
                }
                else
                {
                    var coverLength = files.FileLength(cover.SourcePath!);
                    if (coverLength <= 0)
                    {
                        missing.Add("обложка «" + cover.FileName + "»");
                    }
                    else
                    {
                        // The site's uploader names the picture's own size with every page it
                        // sends, so the size the project already holds travels with the file.
                        coverFile = new PageFile(cover.SourcePath!, coverLength, cover.ImageWidth, cover.ImageHeight);
                    }
                }
            }

            var fileList = new List<PageFile>(pages.Count);
            var spread = 0;
            foreach (var page in pages)
            {
                spread++;

                var length = files.FileLength(page.SourcePath!);
                if (length <= 0)
                {
                    missing.Add("разворот " + spread + " «" + page.FileName + "»");
                    continue;
                }

                fileList.Add(new PageFile(page.SourcePath!, length, page.ImageWidth, page.ImageHeight));
            }

            if (missing.Count > 0)
            {
                problems.Add(name + ": не хватает файлов — " + string.Join(", ", missing) +
                    ". Верните файлы на место или создайте проект заново.");
                return null;
            }

            return new Book(
                product, variant, pagePart.Id, parts, fileList, coverPart?.Id ?? 0, coverFile, source);
        }

        /// <summary>
        /// Says how many spreads the chosen parameters take and how many the book holds.
        /// </summary>
        /// <remarks>
        /// The numbers, not the price: the site answers zero for a set it will not sell, and a
        /// customer told «the combination is unavailable» has nothing to change. The range is
        /// read after the answers because it belongs to the chosen elements - paper on a layflat
        /// starts at ten spreads where cardboard starts at two - and the sentence names both ways
        /// out, editing the book or choosing other parameters.
        /// </remarks>
        private static string OutOfRange(
            string name,
            ProductQuantitySettings quantity,
            QuantityRange range,
            int count)
        {
            var text = new StringBuilder();
            text.Append(name).Append(": для выбранных параметров нужно от ").Append(range.From)
                .Append(" до ").Append(range.To).Append(' ').Append(UnitOf(quantity));

            if (range.Step > 1)
                text.Append(" с шагом ").Append(range.Step);

            return text.Append(", а в книге ").Append(count)
                .Append(". Отредактируйте книгу или выберите другие параметры формата.")
                .ToString();
        }

        private static string UnitOf(ProductQuantitySettings quantity) => quantity.Unit switch
        {
            QuantityUnit.Spreads => "разворотов",
            QuantityUnit.Pages => "страниц",
            _ => "файлов",
        };

        /// <summary>The answers the assembled order sends for one part.</summary>
        private static IReadOnlyDictionary<long, long> SelectionOf(
            IReadOnlyList<SelectedPart> parts,
            long partId)
        {
            for (var index = 0; index < parts.Count; index++)
            {
                if (parts[index].PartId == partId)
                    return parts[index].Selection.Elements;
            }

            return new Dictionary<long, long>();
        }

        /// <summary>
        /// The part that holds the single cover file, or null when the format has no such part.
        /// </summary>
        /// <remarks>
        /// The site's document names the part the spreads go into and leaves the cover part
        /// unnamed: it is the one with a fixed quantity, which the uploader prices as one file.
        /// When more than one part has a fixed quantity the document does not say which is the
        /// cover, and a guess would send the cover into, say, a slipcase.
        /// </remarks>
        private static bool TryCoverPartOf(
            ProductVariant variant,
            string name,
            List<string> problems,
            out ProductPart? coverPart)
        {
            var singleFile = (variant.Parts ?? Array.Empty<ProductPart>())
                .Where(part => !part.HoldsPages)
                .Where(part => part.Quantity?.Default is { } range && range.From == range.To)
                .ToList();

            coverPart = singleFile.Count == 1 ? singleFile[0] : null;

            if (singleFile.Count > 1)
            {
                problems.Add(name + ": у формата несколько частей с одним файлом, какая из них — обложка, не ясно.");
                return false;
            }

            return true;
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
