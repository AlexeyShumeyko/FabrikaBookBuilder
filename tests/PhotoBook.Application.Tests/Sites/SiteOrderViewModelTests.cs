using System;
using System.Linq;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Pricing;
using PhotoBook.Application.Sites.Books.Files;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Application.Sites.Catalog.Layout;
using PhotoBook.Application.Sites.Pricing;
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

            // Assembled books are not a finished order: the group still needs a format.
            Assert.False(page.IsReady);
            Assert.Equal("не выбран", page.Groups[0].FormatText);
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
        public void SaysWhyTheButtonIsOffWhenNoFormatIsChosen()
        {
            var project = TestProject.Combined(TestProject.Book(1, "/c1.jpg", "/p1.jpg"));

            var page = new SiteOrderViewModel();
            page.Prepare(project, "Проект");

            // The project is complete and the site is known, so the only thing missing is the
            // choice of the group - and a grey button with nothing beside it does not say so.
            Assert.False(page.CanUpload);
            Assert.True(page.HasBlockingText);
            Assert.Contains("формат", page.BlockingText, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void KeepsTheProjectsOwnLinesWhenARunSaysSomething()
        {
            var project = TestProject.Combined(TestProject.Book(1, "/c1.jpg", "/p1.jpg"));
            project.AvailableFiles.Add("/left-behind.jpg");

            var page = new SiteOrderViewModel();
            page.Prepare(project);

            Assert.Contains("Фотографий вне книги: 1", page.Message, StringComparison.Ordinal);

            // What the site said replaces the previous answer of the site, never the project's
            // own lines: the card used to appear on the way in and vanish with the price.
            page.ShowMessage("Цена не получена: сайт не ответил.");

            Assert.Contains("Фотографий вне книги: 1", page.Message, StringComparison.Ordinal);
            Assert.Contains("Цена не получена", page.Message, StringComparison.Ordinal);
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
        public async Task ChoosingTheSitesFormatWritesItIntoEveryBookOfTheGroup()
        {
            var project = TestProject.Combined(
                TestProject.Book(1, "/c1.jpg", "/p1.jpg"),
                TestProject.Book(2, "/c2.jpg", "/p2.jpg"));
            var catalog = new StubCatalog(
                new CatalogProduct(4494, "Лайфлат", "kit", 1, new[]
                {
                    new CatalogFormat(9255, "21x30"),
                    new CatalogFormat(9257, "20x20"),
                }));
            var page = new SiteOrderViewModel(catalog, new StubSessionStore());
            page.Prepare(project, "Проект");

            var saves = 0;
            page.ChoiceChanged += (_, _) => saves++;

            await page.LoadFormatsAsync();

            Assert.Equal(2, page.Formats.Count);
            Assert.Equal("Лайфлат 21x30", page.Formats[0].DisplayText);
            Assert.False(page.IsReady);

            // One choice for the group, not one per row: a group is one project on the site.
            page.Groups[0].SelectedFormat = page.Formats[1];

            // A new format is new questions, so the order is ready only once they are read.
            await page.LoadFormatsAsync();

            Assert.Equal(1, saves);
            Assert.All(project.Books, book =>
            {
                Assert.Equal(4494, book.SiteProductId);
                Assert.Equal(9257, book.SiteVariantId);
                Assert.Equal("Лайфлат", book.SiteProductTitle);
                Assert.Equal("20x20", book.SiteVariantTitle);
            });
            Assert.True(page.IsReady);
            Assert.Equal(new[] { "Лайфлат 20x20", "Лайфлат 20x20" },
                page.Books.Select(book => book.FormatText));
            Assert.Equal("Лайфлат 20x20", page.Groups[0].FormatText);
        }

        [Fact]
        public async Task AsksTheFormatsParametersAndRemembersTheAnswers()
        {
            var project = TestProject.Combined(TestProject.Book(1, "/c1.jpg", "/p1.jpg"));
            var catalog = new StubCatalog(
                new CatalogProduct(4494, "Лайфлат", "kit", 1, new[] { new CatalogFormat(9255, "21x30") }))
            {
                Format = TestFormats.WithCondition(),
            };
            var page = new SiteOrderViewModel(catalog, new StubSessionStore());
            page.Prepare(project, "Проект");

            await page.LoadFormatsAsync();
            page.Groups[0].SelectedFormat = page.Formats[0];
            await page.LoadFormatsAsync();

            var block = Assert.Single(page.Groups[0].Parameters);
            var question = Assert.Single(block.Parameters);
            Assert.Equal("Прослойка", question.Title);
            Assert.True(question.IsRequired);
            Assert.False(page.IsReady);

            question.Selected = question.Elements[0];

            Assert.Equal(420268, project.Books[0].SiteOptions[65738]);
            Assert.True(page.IsReady);
        }

        /// <summary>
        /// The list opens onto the file that would be refused: the customer's next question is
        /// which file, and a book that hid it behind a click would make them ask it for every one.
        /// </summary>
        [Fact]
        public async Task OpensABookWhoseFilesTheSiteWouldRefuse()
        {
            var project = TestProject.Combined(TestProject.Book(1, "/c1.jpg", "/p1.jpg"));
            var catalog = new StubCatalog(
                new CatalogProduct(4494, "Лайфлат", "kit", 1, new[] { new CatalogFormat(9255, "21x30") }))
            {
                Format = TestFormats.WithCondition(37556),
            };
            var page = new SiteOrderViewModel(
                catalog,
                new StubSessionStore(),
                new StubQuotes(),
                new StubFiles(),
                Files(new PixelSize(800, 600)));

            page.Prepare(project, "Проект");

            await page.LoadFormatsAsync();
            page.Groups[0].SelectedFormat = page.Formats[0];
            await page.LoadFormatsAsync();
            page.Groups[0].Parameters[0].Parameters[0].Selected = page.Groups[0].Parameters[0].Parameters[0].Elements[0];

            await page.Pricing;
            await page.FileCheck;

            Assert.False(page.CanUpload);

            var book = page.Books[0];
            Assert.True(book.IsFailed);
            Assert.True(book.IsExpanded);
            Assert.Equal("Ошибка в файлах", book.ConditionText);
            Assert.Contains(book.FileRows, file => file.HasProblem);
            Assert.Contains("исправьте", page.Groups[0].FilesText);
        }

        /// <summary>
        /// A file the site would only trim is a question, not a failure: the book stays ready and
        /// opens itself so the customer can see which file the question is about. The book's own
        /// reasons stay empty - the sentence about the file is in the file's row, once.
        /// </summary>
        [Fact]
        public async Task OpensABookWithAQuestionAndKeepsTheSentenceInTheFilesRow()
        {
            var project = TestProject.Combined(TestProject.Book(1, "/c1.jpg", "/p1.jpg"));
            var catalog = new StubCatalog(
                new CatalogProduct(4494, "Лайфлат", "kit", 1, new[] { new CatalogFormat(9255, "21x30") }))
            {
                Format = TestFormats.WithCondition(37556),
            };
            var page = new SiteOrderViewModel(
                catalog,
                new StubSessionStore(),
                new StubQuotes(),
                new StubFiles(),
                Files(new PixelSize(9000, 9000)));

            page.Prepare(project, "Проект");
            await page.LoadFormatsAsync();
            page.Groups[0].SelectedFormat = page.Formats[0];
            await page.LoadFormatsAsync();
            page.Groups[0].Parameters[0].Parameters[0].Selected = page.Groups[0].Parameters[0].Parameters[0].Elements[0];

            await page.Pricing;
            await page.FileCheck;

            var book = page.Books[0];
            Assert.True(page.Groups[0].NeedsConfirmation);
            Assert.False(book.IsFailed);
            Assert.True(book.IsExpanded);
            Assert.Equal(string.Empty, book.ReasonsText);

            var file = Assert.Single(book.FileRows, row => row.Title == "Разворот 1");
            Assert.True(file.IsProblem);
            Assert.False(file.IsRemark);
            Assert.Contains("Разворот 1", file.Problem, StringComparison.Ordinal);
        }

        /// <summary>
        /// A project assembled from two folders: one book exported at 20×20's spread size, the
        /// other at 21×30's. Both carry the same stored format, as a project built beside another
        /// does - so the page sizes are the only thing that tells them apart.
        /// </summary>
        private static Project TwoPageSizes() => TestProject.Combined(
            TestProject.Book(1, (4724, 2398), "/c1.jpg", "/p1.jpg"),
            TestProject.Book(2, (4913, 3496), "/c2.jpg", "/p2.jpg"));

        /// <summary>That project, on a page where both groups chose a format and answered it.</summary>
        private static async Task<SiteOrderViewModel> TwoAnsweredGroupsAsync(
            Project project,
            OrderFileCheckService files)
        {
            var catalog = new StubCatalog(
                new CatalogProduct(4494, "Лайфлат", "kit", 1, new[] { new CatalogFormat(9255, "21x30") }))
            {
                Format = TestFormats.WithCondition(37556),
            };

            var page = new SiteOrderViewModel(
                catalog, new StubSessionStore(), new StubQuotes(), new StubFiles(), files);

            page.Prepare(project, "Разные проекты");
            await page.LoadFormatsAsync();

            foreach (var group in page.Groups)
            {
                group.SelectedFormat = page.Formats[0];
                await group.LoadParametersAsync();
                group.Parameters[0].Parameters[0].Selected =
                    group.Parameters[0].Parameters[0].Elements[0];
            }

            await page.Pricing;
            await page.FileCheck;

            return page;
        }

        /// <summary>
        /// A project assembled from two folders holds two formats, and the site takes one per
        /// project: the page stands them apart before anything is chosen.
        /// </summary>
        [Fact]
        public void LaysABookOrderOutIntoOneGroupPerPageSize()
        {
            var page = new SiteOrderViewModel();
            page.Prepare(TwoPageSizes(), "Разные проекты");

            Assert.Equal(2, page.Groups.Count);
            Assert.Equal(new[] { "Book 1" }, page.Groups[0].Books.Select(book => book.Name));
            Assert.Equal(new[] { "Book 2" }, page.Groups[1].Books.Select(book => book.Name));

            // The books keep the project's own numbering, so the numbers read on the page are the
            // project's numbers rather than each group counting from one.
            Assert.Equal(new[] { 1, 2 }, page.Books.Select(book => book.Number));
        }

        [Fact]
        public void KeepsBooksOfOnePageSizeInOneGroup()
        {
            var project = TestProject.Combined(
                TestProject.Book(1, (4724, 2398), "/c1.jpg", "/p1.jpg"),
                TestProject.Book(2, (4722, 2398), "/c2.jpg", "/p2.jpg"));

            var page = new SiteOrderViewModel();
            page.Prepare(project, "Один формат");

            // A pixel or two of another program's rounding is one format, not two groups.
            Assert.Single(page.Groups);
        }

        [Fact]
        public async Task ChoosesTheFormatOfEachGroupApart()
        {
            var project = TwoPageSizes();
            var catalog = new StubCatalog(
                new CatalogProduct(4494, "Лайфлат", "kit", 1, new[]
                {
                    new CatalogFormat(9255, "21x30"),
                    new CatalogFormat(9257, "20x20"),
                }));

            var page = new SiteOrderViewModel(catalog, new StubSessionStore());
            page.Prepare(project, "Разные проекты");
            await page.LoadFormatsAsync();

            page.Groups[0].SelectedFormat = page.Formats[1];

            // The choice belongs to its group: the second group is still waiting for its own.
            Assert.Equal(9257, project.Books[0].SiteVariantId);
            Assert.Null(project.Books[1].SiteVariantId);
            Assert.Null(page.Groups[1].SelectedFormat);
        }

        /// <summary>
        /// Two ready groups are two projects on the site and one run in the program: the plans of
        /// both go to the send window together.
        /// </summary>
        [Fact]
        public async Task SendsEveryGroupThatCanGoInOneRun()
        {
            var page = await TwoAnsweredGroupsAsync(TwoPageSizes(), Files());

            Assert.All(page.Groups, group => Assert.True(group.CanGo));
            Assert.True(page.CanUpload);
            Assert.False(page.HasWarningText);

            page.Upload();

            Assert.Equal(2, page.PendingUpload!.Groups.Count);
            Assert.Equal(2, page.PendingUpload.Summary.ProjectCount);
        }

        /// <summary>
        /// A group the site would refuse is left out, and said so: the other groups still go,
        /// because the customer assembled several orders in one project.
        /// </summary>
        [Fact]
        public async Task LeavesABrokenGroupOutOfTheRunAndSaysSo()
        {
            var required = Layout().RequiredPixels(94, 10, null)!;
            var page = await TwoAnsweredGroupsAsync(
                TwoPageSizes(),
                FilesByPath(path => path.Contains("p2", StringComparison.Ordinal)
                    ? new PixelSize(800, 600)
                    : required));

            Assert.True(page.Groups[0].CanGo);
            Assert.False(page.Groups[1].CanGo);

            // The send is allowed, and the page says what it leaves behind.
            Assert.True(page.CanUpload);
            Assert.True(page.HasWarningText);
            Assert.Contains("Группа 2", page.WarningText, StringComparison.Ordinal);
            Assert.Contains("не будут", page.WarningText, StringComparison.Ordinal);

            page.Upload();

            Assert.Single(page.PendingUpload!.Groups);
            Assert.Equal(1, page.PendingUpload.Summary.ProjectCount);
            Assert.Contains(
                page.PendingUpload.Notes!,
                note => note.Contains("Группа 2", StringComparison.Ordinal));
        }

        /// <summary>A file check over a layout whose pages are what the site asks for.</summary>
        /// <param name="size">What the disk says a page is; null for the size the site asks.</param>
        private static OrderFileCheckService Files(PixelSize? size = null)
        {
            var layout = Layout();

            return new OrderFileCheckService(
                new FileCatalog(layout),
                new FileInspector(size ?? layout.RequiredPixels(94, 10, null)!));
        }

        /// <summary>A file check whose answer depends on the file, for two groups at once.</summary>
        private static OrderFileCheckService FilesByPath(Func<string, PixelSize> size)
        {
            var layout = Layout();

            return new OrderFileCheckService(new FileCatalog(layout), new FileInspector(size));
        }

        /// <summary>The geometry the site describes for the test's format.</summary>
        private static SiteLayout Layout() => new SiteLayout(
                4494,
                9255,
                300,
                new[]
                {
                    new PartLayout(
                        93,
                        "Обложка",
                        Array.Empty<PageSizeRange>(),
                        new PageSize(410, 290),
                        PrintMargins.None,
                        Array.Empty<SizeIndexCondition>(),
                        PrintMargins.None,
                        0),
                    new PartLayout(
                        94,
                        "Разворот",
                        Array.Empty<PageSizeRange>(),
                        new PageSize(410, 290),
                        PrintMargins.None,
                        Array.Empty<SizeIndexCondition>(),
                        PrintMargins.None,
                        0)
                },
                37556);

        private sealed class FileCatalog : ISiteCatalogService
        {
            private readonly SiteLayout _layout;

            internal FileCatalog(SiteLayout layout) => _layout = layout;

            public Task<Result<SiteLayout>> GetLayoutAsync(
                SiteRegion region,
                long productId,
                long componentId,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<SiteLayout>.Ok(_layout));

            public Task<Result<IReadOnlyList<CatalogProduct>>> GetProductsAsync(
                SiteRegion region,
                CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

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

        private sealed class FileInspector : IBookFileInspector
        {
            private readonly PixelSize? _size;
            private readonly Func<string, PixelSize>? _byPath;

            internal FileInspector(PixelSize size) => _size = size;

            internal FileInspector(Func<string, PixelSize> byPath) => _byPath = byPath;

            public Task<IReadOnlyList<PageFileFacts>> InspectAsync(
                IReadOnlyList<PageFile> files,
                CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<PageFileFacts>>(
                    files.Select(file => new PageFileFacts(
                        file.Path,
                        true,
                        _byPath is null ? _size : _byPath(file.Path))).ToList());
        }

        [Fact]
        public async Task HandsThePricedOrderToTheUploadOnlyWhenAsked()
        {
            var project = TestProject.Combined(TestProject.Book(1, "/c1.jpg", "/p1.jpg"));
            var catalog = new StubCatalog(
                new CatalogProduct(4494, "Лайфлат", "kit", 1, new[] { new CatalogFormat(9255, "21x30") }))
            {
                Format = TestFormats.WithCondition(37556),
            };
            var page = new SiteOrderViewModel(
                catalog, new StubSessionStore(), new StubQuotes(), new StubFiles(), Files());
            page.Prepare(project, "Проект");

            await page.LoadFormatsAsync();
            page.Groups[0].SelectedFormat = page.Formats[0];
            await page.LoadFormatsAsync();
            page.Groups[0].Parameters[0].Parameters[0].Selected = page.Groups[0].Parameters[0].Parameters[0].Elements[0];

            // The price is not a button: the page asks for it as soon as the answers are complete.
            await page.Pricing;

            // So are the files: the plan is what the check reads, and the button waits for both.
            await page.FileCheck;

            var asked = 0;
            page.UploadRequested += (_, _) => asked++;

            Assert.True(page.CanUpload);

            // A book whose files fit says so in one word and stays closed.
            Assert.Equal("Готова", page.Books[0].ConditionText);
            Assert.False(page.Books[0].IsExpanded);

            page.Upload();

            Assert.Equal(1, asked);
            Assert.NotNull(page.PendingUpload);
            var group = Assert.Single(page.PendingUpload!.Groups);
            Assert.Equal(37556, group.Request.IntegrationId);
            Assert.Equal(1, page.PendingUpload.Summary.ProjectCount);
        }

        [Fact]
        public async Task ForgetsThePlanWhenAChoiceChanges()
        {
            var project = TestProject.Combined(TestProject.Book(1, "/c1.jpg", "/p1.jpg"));
            var catalog = new StubCatalog(
                new CatalogProduct(4494, "Лайфлат", "kit", 1, new[] { new CatalogFormat(9255, "21x30") }))
            {
                Format = TestFormats.WithCondition(37556),
            };
            var page = new SiteOrderViewModel(
                catalog, new StubSessionStore(), new StubQuotes(), new StubFiles(), Files());
            page.Prepare(project, "Проект");

            await page.LoadFormatsAsync();
            page.Groups[0].SelectedFormat = page.Formats[0];
            await page.LoadFormatsAsync();
            page.Groups[0].Parameters[0].Parameters[0].Selected = page.Groups[0].Parameters[0].Parameters[0].Elements[0];
            await page.Pricing;
            await page.FileCheck;

            Assert.True(page.CanUpload);

            // The site was priced for one combination; another one has no price yet.
            page.Groups[0].Parameters[0].Parameters[0].Selected = null;

            Assert.False(page.CanUpload);
            Assert.Null(page.PendingUpload);
        }

        [Fact]
        public async Task PricesTheOrderItselfAndShowsTheSitesTotal()
        {
            var project = TestProject.Combined(TestProject.Book(1, "/c1.jpg", "/p1.jpg"));
            var catalog = new StubCatalog(
                new CatalogProduct(4494, "Лайфлат", "kit", 1, new[] { new CatalogFormat(9255, "21x30") }))
            {
                Format = TestFormats.WithCondition(),
            };
            var quotes = new StubQuotes();
            var page = new SiteOrderViewModel(catalog, new StubSessionStore(), quotes, new StubFiles());
            page.Prepare(project, "Проект");

            await page.LoadFormatsAsync();
            page.Groups[0].SelectedFormat = page.Formats[0];
            await page.LoadFormatsAsync();
            page.Groups[0].Parameters[0].Parameters[0].Selected = page.Groups[0].Parameters[0].Parameters[0].Elements[0];

            // Nothing was pressed: the total is there because the answers are complete.
            await page.Pricing;

            // One settled set of answers costs one request, not one per answer or per repaint.
            Assert.Equal(1, quotes.Calls);

            // The price is the site's own text, and the total is the same number written with
            // the site's currency, so both name the sum and the currency rather than a layout.
            Assert.Contains("RUB", page.Books[0].PriceText, StringComparison.Ordinal);
            Assert.Contains("1 024", page.Books[0].PriceText, StringComparison.Ordinal);
            Assert.Contains("024,00", page.TotalText, StringComparison.Ordinal);
            Assert.Contains("RUB", page.TotalText, StringComparison.Ordinal);
            Assert.True(page.HasTotal);
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

        /// <summary>
        /// Nothing is wrong with a whole project that has no format yet: the format is the next
        /// thing the customer picks, in the panel that asks for it. The page used to print a
        /// sentence asking for it, which appeared with the catalog and vanished with the price.
        /// </summary>
        [Fact]
        public async Task SaysNothingWhenTheOnlyThingMissingIsTheFormat()
        {
            var catalog = new StubCatalog(
                new CatalogProduct(4494, "Лайфлат", "kit", 1, new[] { new CatalogFormat(9255, "21x30") }));
            var page = new SiteOrderViewModel(catalog, new StubSessionStore());
            page.Prepare(TestProject.Combined(TestProject.Book(1, "/c1.jpg", "/p1.jpg")), "Проект");

            await page.LoadFormatsAsync();

            Assert.False(page.IsReady);
            Assert.False(page.HasMessage);
            Assert.True(page.HasFormats);
            Assert.Null(page.Groups[0].SelectedFormat);
            Assert.Equal("не выбран", page.Groups[0].FormatText);
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

    /// <summary>One format as the uploader describes it: a cover, spreads and one question.</summary>
    internal static class TestFormats
    {
        public static SiteProduct WithCondition(long integration = 0) => new(
            4494,
            string.Empty,
            "Лайфлат",
            "kit",
            new[]
            {
                new ProductVariant(9255, "21x30", Array.Empty<ProductOption>(), new[]
                {
                    new ProductPart(93, "Обложка", Array.Empty<ProductOption>(),
                        new ProductQuantitySettings(string.Empty, QuantityUnit.Copies,
                            new QuantityRange(1, 1, 1), false, Array.Empty<QuantityRule>())),
                    new ProductPart(94, "Разворот", new[]
                    {
                        new ProductOption(65738, "Прослойка", true, null, Array.Empty<string>(),
                            null, Array.Empty<string>(), new[] { new OptionElement(420268, "Белая") }),
                    }, new ProductQuantitySettings(string.Empty, QuantityUnit.Spreads,
                        new QuantityRange(1, 60, 1), false, Array.Empty<QuantityRule>()), true),
                }),
            },
            integration);
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

        /// <summary>The document the site sends for a chosen format, if the test has one.</summary>
        public SiteProduct? Format { get; init; }

        public Task<Result<SiteProduct>> GetFormatAsync(
            SiteRegion region,
            long productId,
            long componentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Format is null
                ? Result<SiteProduct>.Fail("Формат не открылся.")
                : Result<SiteProduct>.Ok(Format));

        /// <summary>The geometry the test has, if it needs one.</summary>
        public SiteLayout? Geometry { get; init; }

        public Task<Result<SiteLayout>> GetLayoutAsync(
            SiteRegion region,
            long productId,
            long componentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Geometry is null
                ? Result<SiteLayout>.Fail("Геометрия не открылась.")
                : Result<SiteLayout>.Ok(Geometry));

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

    internal sealed class StubQuotes : IGroupQuoteService
    {
        /// <summary>How many times the page asked the site for a price.</summary>
        internal int Calls { get; private set; }

        public Task<GroupQuote> QuoteAsync(
            SiteSession session,
            IReadOnlyList<PhotoBook.Application.Sites.Books.Book> books,
            CancellationToken cancellationToken = default)
        {
            Calls++;

            return Task.FromResult(new GroupQuote(books
                .Select((book, index) => new BookQuote(index + 1, book,
                    SitePriceCheck.Valid(new SitePriceQuote(1024m, "1 024,00 RUB", "RUB"))))
                .ToList()));
        }
    }

    /// <summary>The disk, as the order needs it: a file with a length and nothing else.</summary>
    internal sealed class StubFiles : IFileService
    {
        public Task<List<string>> GetJpegFilesAsync(string folderPath) =>
            Task.FromResult(new List<string>());

        public Task<ValidationResult> ValidateFoldersDetailedAsync(List<string> folderPaths) =>
            Task.FromResult(new ValidationResult());

        public Task CopyFileAsync(string source, string destination) => Task.CompletedTask;

        public bool IsJpegFile(string filePath) => true;

        public bool DirectoryExists(string path) => true;

        public bool FileExists(string path) => true;

        public long FileLength(string path) => 1024;

        public void EnsureDirectory(string path)
        {
        }
    }
}
