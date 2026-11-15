using System;
using System.Linq;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Core;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Application.Tests.Sites
{
    /// <summary>
    /// The order page is reached from the project's panel and shows what would go to the site.
    /// Its job is to be honest on the way in: a project that is not assembled is shown with the
    /// reason, and the customer learns it before anything is created on the site.
    /// </summary>
    public class SiteOrderViewModelTests
    {
        [Fact]
        public void ListsTheBooksOfAnAssembledProject()
        {
            var project = TestProject.Combined(
                TestProject.Book(1, "/c1.jpg", "/p1.jpg", "/p2.jpg"),
                TestProject.Book(2, "/c2.jpg", "/p3.jpg", "/p4.jpg"));

            var page = new SiteOrderViewModel();
            page.Prepare(project, "Лето 2026");

            // Assembled books are not a finished order: each one still needs a format.
            Assert.False(page.IsReady);
            Assert.Equal("без формата: 2", page.ChosenFormatText);
            Assert.Equal(new[] { "не выбран", "не выбран" }, page.Books.Select(book => book.FormatText));
            Assert.Equal("Лето 2026", page.ProjectName);
            Assert.Equal(2, page.Books.Count);
            Assert.Equal(4, page.SpreadCount);
            Assert.Equal("2", page.BookCountText);
            Assert.All(page.Books, book => Assert.True(book.IsReady));
            Assert.Equal(new[] { "Book 1", "Book 2" }, page.Books.Select(book => book.Name));
            Assert.Equal(new[] { "Готова", "Готова" }, page.Books.Select(book => book.ConditionText));
        }

        [Fact]
        public void ShowsAHalfFilledBookAndWhyItCannotGo()
        {
            var project = TestProject.Combined(
                TestProject.Book(1, "/c1.jpg", "/p1.jpg", "/p2.jpg"),
                TestProject.Book(2, "/c2.jpg", "/p3.jpg", string.Empty));

            var page = new SiteOrderViewModel();
            page.Prepare(project);

            Assert.False(page.IsReady);
            Assert.True(page.HasMessage);
            Assert.Contains("заполнено 2 из 3 слотов", page.Message, StringComparison.Ordinal);
            Assert.Equal("Не хватает фотографий", page.Books[1].ConditionText);
            // A project without a name is still shown as a project rather than as an empty line.
            Assert.Equal("Проект", page.ProjectName);
        }

        [Fact]
        public void SaysABookHasNoPhotosAtAll()
        {
            var project = TestProject.Combined(TestProject.Book(1, string.Empty));

            var page = new SiteOrderViewModel();
            page.Prepare(project);

            Assert.False(page.IsReady);
            Assert.Contains("нет ни одной фотографии", page.Message, StringComparison.Ordinal);
            Assert.Equal(BookCondition.NoPhotos, page.Books[0].Condition);
        }

        [Fact]
        public void CountsPhotosTheOrderWillNotCarry()
        {
            var project = TestProject.Combined(TestProject.Book(1, "/c1.jpg", "/p1.jpg"));
            project.AvailableFiles.Add("/left-behind.jpg");

            var page = new SiteOrderViewModel();
            page.Prepare(project);

            Assert.Contains("Фотографий вне книги: 1", page.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ChoosingTheSitesFormatWritesItIntoTheBookAndCompletesTheOrder()
        {
            var project = TestProject.Combined(TestProject.Book(1, "/c1.jpg", "/p1.jpg"));
            var catalog = new StubCatalog(
                new CatalogProduct(4494, "Лайфлат", "kit", 1, new[]
                {
                    new CatalogFormat(9255, "21x30"),
                    new CatalogFormat(9257, "20x20"),
                }));
            var page = new SiteOrderViewModel(catalog, new StubSessionStore());
            page.Prepare(project, "Проект");

            var saves = 0;
            page.FormatChosen += (_, _) => saves++;

            await page.LoadFormatsAsync();

            Assert.Equal(2, page.Books[0].Formats.Count);
            Assert.Equal("Лайфлат 21x30", page.Books[0].Formats[0].DisplayText);
            Assert.False(page.IsReady);

            page.Books[0].SelectedFormat = page.Books[0].Formats[1];

            Assert.Equal(1, saves);
            Assert.Equal(4494, project.Books[0].SiteProductId);
            Assert.Equal(9257, project.Books[0].SiteVariantId);
            Assert.Equal("Лайфлат", project.Books[0].SiteProductTitle);
            Assert.Equal("20x20", project.Books[0].SiteVariantTitle);
            Assert.True(page.IsReady);
            Assert.Equal("Лайфлат 20x20", page.Books[0].FormatText);
            Assert.Equal("у всех книг выбран формат", page.ChosenFormatText);
        }

        [Fact]
        public async Task KeepsTheBooksAndExplainsWhenTheSitesCatalogDoesNotOpen()
        {
            var project = TestProject.Combined(TestProject.Book(1, "/c1.jpg", "/p1.jpg"));
            var page = new SiteOrderViewModel(new StubCatalog(failure: "Сайт не ответил."), new StubSessionStore());
            page.Prepare(project, "Проект");

            await page.LoadFormatsAsync();

            Assert.Single(page.Books);
            Assert.Empty(page.Books[0].Formats);
            Assert.Equal("Сайт не ответил.", page.Message);
        }

        [Fact]
        public void SaysAFormatMustBeChosenBeforeAnAssembledBookCanGo()
        {
            var page = new SiteOrderViewModel();
            page.Prepare(TestProject.Combined(TestProject.Book(1, "/c1.jpg", "/p1.jpg")), "Проект");

            Assert.False(page.IsReady);
            Assert.True(page.HasMessage);
            Assert.Contains("Выберите формат", page.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AnUnopenedProjectHasNothingToShow()
        {
            var page = new SiteOrderViewModel();
            page.Prepare(null);

            Assert.False(page.IsReady);
            Assert.Empty(page.Books);
            Assert.Equal("Проект не открыт.", page.Message);
        }

        [Fact]
        public void GoingBackAsksTheShell()
        {
            var page = new SiteOrderViewModel();
            bool asked = false;
            page.BackRequested += (_, _) => asked = true;

            page.Back();

            Assert.True(asked);
        }
    }

    internal sealed class StubCatalog : ISiteCatalogService
    {
        private readonly IReadOnlyList<CatalogProduct> _products;
        private readonly string? _failure;

        public StubCatalog(params CatalogProduct[] products) => _products = products;

        public StubCatalog(string failure)
        {
            _products = Array.Empty<CatalogProduct>();
            _failure = failure;
        }

        public Task<Result<IReadOnlyList<CatalogProduct>>> GetProductsAsync(
            SiteRegion region,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_failure is null
                ? Result<IReadOnlyList<CatalogProduct>>.Ok(_products)
                : Result<IReadOnlyList<CatalogProduct>>.Fail(_failure));

        public Task<Result<SiteProduct>> GetFormatAsync(
            SiteRegion region,
            long productId,
            long componentId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<SiteProduct>> GetProductAsync(
            SiteRegion region,
            string slug,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    internal sealed class StubSessionStore : ISiteSessionStore
    {
        private SiteSession? _session = new(SiteRegion.Com, "token", DateTimeOffset.UtcNow.AddHours(1));

        public SiteSession? Load() => _session;

        public void Save(SiteSession session) => _session = session;

        public void Clear() => _session = null;
    }
}
