using System;
using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application;
using PhotoBook.Application.Sites.Books.Order;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Infrastructure;

namespace PhotoBook.Application.Tests.Sites.Books.Order
{
    /// <summary>
    /// The draft is where the project and the catalog meet. What it cannot join it says, because
    /// a book sent into the wrong part of a product is priced as one book and delivered as
    /// another.
    /// </summary>
    public class OrderDraftTests : IDisposable
    {
        private const long Product = 4494;
        private const long Variant = 9255;
        private const long SpreadPart = 37556;

        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "PhotoBookRenamer.Tests", Guid.NewGuid().ToString("N"));

        private readonly FileService _files = new();

        public OrderDraftTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        [Fact]
        public void BuildsTheBooksThatHaveAFormat()
        {
            var project = ProjectWith(Book(Page(1), Page(2)));

            var draft = OrderDraft.From(project, new[] { Catalog() }, _files);

            var book = Assert.Single(draft.Books);
            Assert.True(draft.IsComplete);
            Assert.Empty(draft.Problems);
            Assert.Equal(2, book.Files.Count);
            Assert.Equal("/photos/1.jpg".Length > 0 ? book.Files[0].Path : string.Empty, book.Files[0].Path);
            Assert.True(book.Files[0].LengthBytes > 0);
            Assert.Equal(2, book.PageSlots);
            Assert.Equal(SpreadPart, book.PagePartId);
        }

        [Fact]
        public void SaysWhichBookHasNoFormatAndStillBuildsTheOthers()
        {
            var unformatted = new PhotoBook.Core.Book { Name = "Без формата" };
            var project = ProjectWith(Book(Page(1)), unformatted);

            var draft = OrderDraft.From(project, new[] { Catalog() }, _files);

            Assert.Single(draft.Books);
            Assert.False(draft.IsComplete);
            Assert.Contains(draft.Problems, problem => problem.Contains("Без формата") && problem.Contains("формат не выбран"));
        }

        [Fact]
        public void SaysWhenTheChosenFormatIsNoLongerInTheCatalog()
        {
            var project = ProjectWith(Book(Page(1)));

            var draft = OrderDraft.From(project, Array.Empty<SiteProduct>(), _files);

            Assert.Empty(draft.Books);
            Assert.Contains(draft.Problems, problem => problem.Contains("нет в каталоге"));
        }

        [Fact]
        public void RefusesAFormatWhoseSpreadPartCannotBeToldApart()
        {
            var ambiguous = CatalogWithParts(
                Part(1, "Развороты", QuantityUnit.Spreads),
                Part(2, "Страницы", QuantityUnit.Pages));

            var draft = OrderDraft.From(ProjectWith(Book(Page(1))), new[] { ambiguous }, _files);

            Assert.Empty(draft.Books);
            Assert.Contains(draft.Problems, problem => problem.Contains("несколько частей"));
        }

        [Fact]
        public void RefusesABookWhosePhotographIsGone()
        {
            var project = ProjectWith(Book(Page(1)));

            // The page points at a file that was never written, which is what a moved folder
            // looks like to the program.
            var page = project.Books[0].Pages[0];
            page.SourcePath = System.IO.Path.Combine(_root, "gone.jpg");

            var draft = OrderDraft.From(project, new[] { Catalog() }, _files);

            Assert.Empty(draft.Books);
            Assert.Contains(draft.Problems, problem => problem.Contains("файл не найден"));
        }

        [Fact]
        public void SaysWhenTheBookDoesNotFitTheFormatsRange()
        {
            var narrow = Catalog(from: 2, to: 3);

            var draft = OrderDraft.From(ProjectWith(Book(Page(1))), new[] { narrow }, _files);

            Assert.Empty(draft.Books);
            Assert.Contains(draft.Problems, problem => problem.Contains("принимает от 2 до 3"));
        }

        [Fact]
        public void SaysAnEmptyProjectHasNothingToSend()
        {
            var draft = OrderDraft.From(new PhotoBook.Core.Project(), Array.Empty<SiteProduct>(), _files);

            Assert.Empty(draft.Books);
            Assert.Contains(draft.Problems, problem => problem.Contains("нет ни одной книги"));
        }

        private static string Page(int number) => "/photos/" + number + ".jpg";

        private PhotoBook.Core.Book Book(params string[] pages)
        {
            var book = new PhotoBook.Core.Book
            {
                Name = "Книга 1",
                BookIndex = 0,
                SiteProductId = Product,
                SiteVariantId = Variant
            };

            var index = 1;
            foreach (var page in pages)
            {
                var path = System.IO.Path.Combine(_root, System.IO.Path.GetFileName(page));
                System.IO.File.WriteAllBytes(path, new byte[16]);

                book.Pages.Add(new PhotoBook.Core.Page
                {
                    SourcePath = path,
                    FileName = System.IO.Path.GetFileName(path),
                    Index = index,
                    DisplayIndex = index
                });

                index++;
            }

            book.UpdatePageSlots();
            return book;
        }

        private static PhotoBook.Core.Project ProjectWith(params PhotoBook.Core.Book[] books)
        {
            var project = new PhotoBook.Core.Project { Mode = PhotoBook.Core.AppMode.UniqueFolders };
            foreach (var book in books) project.Books.Add(book);
            return project;
        }

        private static ProductQuantitySettings Counts(QuantityUnit unit, int from = 1, int to = 60) =>
            new(unit == QuantityUnit.Spreads ? "Количество разворотов" : "Количество страниц",
                unit, new QuantityRange(from, to, 1), false, Array.Empty<QuantityRule>());

        /// <summary>
        /// A part as the product page describes it: the site counts it in spreads or pages, and
        /// that is where the photographs go.
        /// </summary>
        private static ProductPart Part(
            long id,
            string title,
            QuantityUnit unit,
            int from = 1,
            int to = 60) =>
            new(
                id,
                title,
                new List<ProductOption>(),
                Counts(unit, from, to),
                unit is QuantityUnit.Spreads or QuantityUnit.Pages);

        private static SiteProduct CatalogWithParts(params ProductPart[] parts)
        {
            var variant = new ProductVariant(Variant, "21×30", new List<ProductOption>(), parts);
            return new SiteProduct(Product, "layflat", "Фотокнига", "photo-book", new[] { variant });
        }

        private static SiteProduct Catalog(
            ProductPart? part = null,
            int from = 1,
            int to = 60)
        {
            var pagePart = part ?? Part(SpreadPart, "Разворот", QuantityUnit.Spreads, from, to);

            var variant = new ProductVariant(
                Variant,
                "21×30",
                new List<ProductOption>(),
                new[] { pagePart });

            return new SiteProduct(Product, "layflat", "Фотокнига", "photo-book", new[] { variant });
        }
    }
}
