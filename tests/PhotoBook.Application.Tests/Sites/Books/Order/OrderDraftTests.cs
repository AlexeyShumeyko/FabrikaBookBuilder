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
        public void SaysWhichParameterOfTheFormatIsStillUnanswered()
        {
            var demanding = Catalog(part: new ProductPart(
                SpreadPart,
                "Разворот",
                new List<ProductOption>
                {
                    new(65738, "Прослойка", true, null, Array.Empty<string>(), null,
                        Array.Empty<string>(), new[] { new OptionElement(420268, "Белая") }),
                },
                Counts(QuantityUnit.Spreads),
                true));

            var draft = OrderDraft.From(ProjectWith(Book(Page(1))), new[] { demanding }, _files);

            Assert.Empty(draft.Books);
            Assert.Contains(draft.Problems, problem => problem.Contains("Прослойка") &&
                problem.Contains("не выбран параметр"));
        }

        [Fact]
        public void DoesNotDemandAParameterTheSiteExcludesForTheChosenAnswer()
        {
            // The interlayer for cardboard is required in the product, but the site excludes it
            // once paper is chosen: a book answering every question it is actually asked is
            // complete, and refusing to price it left a finished book without a price.
            var format = Catalog(part: new ProductPart(
                SpreadPart,
                "Разворот",
                new List<ProductOption>
                {
                    new(65075, "Плотность разворотов", true, null, Array.Empty<string>(), null,
                        Array.Empty<string>(), new[]
                        {
                            new OptionElement(417892, "Картон"),
                            new OptionElement(417891, "Бумага"),
                        }),
                    // As the site states it for the interlayer of cardboard: required, and
                    // excluded as soon as paper is the answer to the density.
                    new(65740, "Прослойка разворотов", true, null, Array.Empty<string>(), "65075",
                        new[] { "417891" }, new[] { new OptionElement(420271, "Прослойка 2 мм") }),
                },
                Counts(QuantityUnit.Spreads),
                true));

            var book = Book(Page(1));
            book.SetSiteOption(65075, 417891);

            var draft = OrderDraft.From(ProjectWith(book), new[] { format }, _files);

            Assert.True(draft.IsComplete);
            Assert.Empty(draft.Problems);
        }

        [Fact]
        public void SendsTheChosenParametersAndTheQuantityOfEveryPart()
        {
            var format = CatalogWithParts(
                new ProductPart(93, "Обложка", new List<ProductOption>
                {
                    new(70519, "Ламинация обложки", true, null, Array.Empty<string>(), null,
                        Array.Empty<string>(), new[] { new OptionElement(450642, "Глянцевая") }),
                }, new ProductQuantitySettings(string.Empty, QuantityUnit.Copies,
                    new QuantityRange(1, 1, 1), false, Array.Empty<QuantityRule>())),
                Part(SpreadPart, "Разворот", QuantityUnit.Spreads));

            var book = Book(Page(1));
            book.SetSiteOption(70519, 450642);
            GiveCover(book);

            var draft = OrderDraft.From(ProjectWith(book), new[] { format }, _files);

            Assert.True(draft.IsComplete);
            var order = Assert.Single(draft.Books);
            Assert.Equal(93, order.Parts[0].PartId);
            Assert.Equal(1, order.Parts[0].Selection.Quantity);
            Assert.Equal(450642, order.Parts[0].Selection.ElementOf(70519));
            Assert.Equal(1, order.Parts[1].Selection.Quantity);
        }

        [Fact]
        public void SendsTheCoverFirstAndTheSpreadsAfterIt()
        {
            var book = Book(Page(1), Page(2));
            GiveCover(book);

            var draft = OrderDraft.From(ProjectWith(book), new[] { CoverCatalog() }, _files);

            var order = Assert.Single(draft.Books);
            Assert.Equal(93, order.CoverPartId);
            Assert.True(order.HasCover);
            Assert.Equal(3, order.SentFileCount);
            Assert.Equal(3, order.AllFiles.Count);
            Assert.EndsWith("cover.jpg", order.AllFiles[0].Path, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("1.jpg", order.AllFiles[1].Path, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, order.Files.Count);
        }

        [Fact]
        public void RefusesABookWhoseCoverWasNotPutIn()
        {
            var draft = OrderDraft.From(
                ProjectWith(Book(Page(1))), new[] { CoverCatalog() }, _files);

            Assert.Empty(draft.Books);
            Assert.Contains(draft.Problems, problem => problem.Contains("обложка"));
        }

        [Fact]
        public void RefusesAFormatWithTwoPartsOfOneFileEach()
        {
            var format = CatalogWithParts(
                new ProductPart(93, "Обложка", new List<ProductOption>(),
                    new ProductQuantitySettings(string.Empty, QuantityUnit.Copies, new QuantityRange(1, 1, 1), false,
                        Array.Empty<QuantityRule>())),
                new ProductPart(95, "Футляр", new List<ProductOption>(),
                    new ProductQuantitySettings(string.Empty, QuantityUnit.Copies, new QuantityRange(1, 1, 1), false,
                        Array.Empty<QuantityRule>())),
                Part(SpreadPart, "Разворот", QuantityUnit.Spreads));

            var draft = OrderDraft.From(ProjectWith(Book(Page(1))), new[] { format }, _files);

            Assert.Empty(draft.Books);
            Assert.Contains(draft.Problems, problem => problem.Contains("несколько частей с одним файлом"));
        }

        [Fact]
        public void RefusesAParameterChoiceTheFormatDoesNotOffer()
        {
            var format = Catalog(part: new ProductPart(
                SpreadPart,
                "Разворот",
                new List<ProductOption>
                {
                    new(65738, "Прослойка", true, null, Array.Empty<string>(), null,
                        Array.Empty<string>(), new[] { new OptionElement(420268, "Белая") }),
                },
                Counts(QuantityUnit.Spreads),
                true));

            var book = Book(Page(1));
            book.SetSiteOption(65738, 420269);

            var draft = OrderDraft.From(ProjectWith(book), new[] { format }, _files);

            Assert.Empty(draft.Books);
            Assert.Contains(draft.Problems, problem => problem.Contains("Прослойка") &&
                problem.Contains("неизвестен"));
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

        /// <summary>Puts a cover file into the book, the way the customer fills the cover slot.</summary>
        private void GiveCover(PhotoBook.Core.Book book)
        {
            var path = System.IO.Path.Combine(_root, "cover.jpg");
            System.IO.File.WriteAllBytes(path, new byte[16]);

            book.Cover = new PhotoBook.Core.Page
            {
                SourcePath = path,
                FileName = "cover.jpg",
                IsCover = true
            };
        }

        /// <summary>A format with a cover part of one file and the spreads, as the site describes it.</summary>
        private static SiteProduct CoverCatalog() =>
            CatalogWithParts(
                new ProductPart(93, "Обложка", new List<ProductOption>(),
                    new ProductQuantitySettings(string.Empty, QuantityUnit.Copies, new QuantityRange(1, 1, 1), false,
                        Array.Empty<QuantityRule>())),
                Part(SpreadPart, "Разворот", QuantityUnit.Spreads));

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
