using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Files;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Application.Sites.Catalog.Layout;
using PhotoBook.Application.Sites.Projects;
using PhotoBook.Application.Sites.Upload;

namespace PhotoBook.Application.Tests.Sites.Books.Files
{
    /// <summary>
    /// The batch check of 4.3 reads every book of the plan in turn, keeps going past a book the
    /// site could not describe, and stops when the customer cancels. Each of those is a decision
    /// the screen relies on, so each is pinned here rather than left to the window.
    /// </summary>
    public class OrderFileCheckTests
    {
        private const long Product = 4494;
        private const long Layflat = 9255;
        private const long PagePart = 94;
        private const long CoverPart = 93;

        [Fact]
        public async Task BlocksABookWhosePhotoIsSmallerThanThePage()
        {
            var book = Group(PageFile("1.jpg"), PageFile("2.jpg"));

            var check = await Service(SpreadLayout(410, 290), new PixelSize(800, 600))
                .CheckAsync(new[] { book });

            var checkedBook = Assert.Single(check.Books);
            Assert.Equal(2, checkedBook.Alerts.Count);
            Assert.All(checkedBook.Alerts, alert => Assert.Equal(FileAlertLevel.Blocking, alert.Level));
            Assert.False(check.CanSend);
            Assert.Equal(2, check.BlockingCount);
        }

        [Fact]
        public async Task SaysNothingWhenEveryPhotoIsThePage()
        {
            var layout = SpreadLayout(410, 290);
            var rule = PageImageRule.For(layout.RequiredPixels(PagePart, 10, null)!, 0, layout.ResolutionDpi);

            var check = await Service(layout, rule.Required).CheckAsync(new[] { Group(PageFile("1.jpg")) });

            Assert.True(check.CanSend);
            Assert.Empty(Assert.Single(check.Books).Alerts);
        }

        [Fact]
        public async Task ChecksTheOtherBooksWhenOneFormatsGeometryIsMissing()
        {
            var missing = Group(PageFile("1.jpg"));
            var present = Group(PageFile("2.jpg"));
            var layout = SpreadLayout(410, 290);
            var required = PageImageRule.For(
                layout.RequiredPixels(PagePart, 10, null)!, 0, layout.ResolutionDpi).Required;

            var check = await ServiceFor(call => call == 1 ? null : layout, required)
                .CheckAsync(new[] { missing, present });

            Assert.Equal(2, check.Books.Count);
            Assert.Equal(FileAlertLevel.Blocking, Assert.Single(check.Books[0].Alerts).Level);
            Assert.DoesNotContain(check.Books[1].Alerts, alert => alert.Level == FileAlertLevel.Blocking);
            Assert.False(check.CanSend);
        }

        [Fact]
        public async Task ReportsProgressForEveryBookAndFinishesOnTheLast()
        {
            var reports = new List<FileCheckProgress>();
            var layout = SpreadLayout(410, 290);
            var rule = PageImageRule.For(layout.RequiredPixels(PagePart, 10, null)!, 0, layout.ResolutionDpi);

            await Service(layout, rule.Required).CheckAsync(
                new[] { Group(PageFile("1.jpg")), Group(PageFile("2.jpg")) },
                new ImmediateProgress(reports));

            Assert.Equal(3, reports.Count);
            Assert.Equal((0, 1), (reports[0].BooksDone, reports[0].BookNumber));
            Assert.Equal((1, 2), (reports[1].BooksDone, reports[1].BookNumber));
            Assert.Equal((2, 2), (reports[2].BooksDone, reports[2].BooksTotal));
        }

        [Fact]
        public async Task StopsBeforeTheFirstBookWhenTheCustomerCancels()
        {
            var inspector = new StubInspector(new PixelSize(4913, 3495));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new OrderFileCheckService(new StubCatalog(SpreadLayout(410, 290)), inspector)
                    .CheckAsync(new[] { Group(PageFile("1.jpg")) }, null, new CancellationToken(true)));

            Assert.Equal(0, inspector.Calls);
        }

        [Fact]
        public async Task AsksTheSiteForTheGeometryOfEachBookOnce()
        {
            var catalog = new StubCatalog(SpreadLayout(410, 290));
            var inspector = new StubInspector(new PixelSize(4913, 3495));

            await new OrderFileCheckService(catalog, inspector)
                .CheckAsync(new[] { Group(PageFile("1.jpg")), Group(PageFile("2.jpg")) });

            Assert.Equal(2, catalog.Calls);
        }

        [Fact]
        public async Task ChecksTheCoverAgainstTheCoverPartAndTheSpreadsAgainstTheirs()
        {
            var layout = LayoutWithCover(1827.5, 1299.5);
            var cover = layout.RequiredPixels(CoverPart, 1, null)!;
            var spread = layout.RequiredPixels(PagePart, 10, null)!;

            var check = await new OrderFileCheckService(
                    new StubCatalog(layout),
                    new StubInspector(spread, new Dictionary<string, PixelSize>
                    {
                        ["cover.jpg"] = cover,
                        ["1.jpg"] = spread,
                    }))
                .CheckAsync(new[] { GroupWithCover(PageFile("1.jpg")) });

            Assert.True(check.CanSend);
            Assert.Empty(Assert.Single(check.Books).Alerts);
        }

        /// <summary>
        /// A project holds several books, and the site refuses the whole project over one bad
        /// file in any of them: checking only the first book would hide the others.
        /// </summary>
        [Fact]
        public async Task ChecksEveryBookOfAProjectAndNotOnlyTheFirst()
        {
            var layout = SpreadLayout(410, 290);
            var spread = layout.RequiredPixels(PagePart, 10, null)!;

            var good = BookOf(PageFile("1.jpg"));
            var bad = BookOf(PageFile("2.jpg"));

            var check = await new OrderFileCheckService(
                    new StubCatalog(layout),
                    new StubInspector(spread, new Dictionary<string, PixelSize>
                    {
                        ["1.jpg"] = spread,
                        ["2.jpg"] = new PixelSize(800, 600),
                    }))
                .CheckAsync(new[] { GroupOf(good, bad) });

            Assert.Equal(2, check.Books.Count);
            Assert.Equal(new[] { 1, 2 }, check.Books.Select(book => book.Number).ToArray());
            Assert.Empty(check.Books[0].Alerts);
            Assert.Single(check.Books[1].Alerts);
            Assert.Equal(1, check.BlockingCount);
            Assert.False(check.CanSend);
        }

        [Fact]
        public async Task BlocksACoverThatIsTheSizeOfASpread()
        {
            var layout = LayoutWithCover(1827.5, 1299.5);
            var spread = layout.RequiredPixels(PagePart, 10, null)!;

            var check = await new OrderFileCheckService(
                    new StubCatalog(layout),
                    new StubInspector(spread))
                .CheckAsync(new[] { GroupWithCover(PageFile("1.jpg")) });

            var alert = Assert.Single(check.Books[0].Alerts);
            Assert.Equal(FileAlertLevel.Blocking, alert.Level);
            Assert.Null(alert.PageNumber);
            Assert.StartsWith("Обложка: ", alert.Message, StringComparison.Ordinal);
            Assert.False(check.CanSend);
        }

        [Fact]
        public async Task SizesTheCoverByTheNumberOfSpreadsAsTheSiteDoes()
        {
            var layout = IndexedCoverLayout();
            var elements = new Dictionary<long, long> { [26] = 4 };
            var cover = layout.RequiredPixels(
                CoverPart,
                1,
                elements,
                new Dictionary<long, int> { [PagePart] = 10, [CoverPart] = 1 })!;
            var spread = layout.RequiredPixels(PagePart, 10, null)!;

            var check = await new OrderFileCheckService(
                    new StubCatalog(layout),
                    new StubInspector(spread, new Dictionary<string, PixelSize>
                    {
                        ["cover.jpg"] = cover,
                        ["1.jpg"] = spread,
                    }))
                .CheckAsync(new[] { GroupWithCover(elements, PageFile("1.jpg")) });

            Assert.True(check.CanSend);
            Assert.Empty(Assert.Single(check.Books).Alerts);
        }

        /// <summary>
        /// The site sizes some option combinations and not others. The cover is the only file
        /// allowed to go unchecked by size: the upload is the site's own check, and refusing the
        /// whole order over a remark would hide the books that are ready.
        /// </summary>
        [Fact]
        public async Task WarnsButLetsTheOrderGoWhenTheSiteDoesNotSizeTheCover()
        {
            var layout = IndexedCoverLayout();
            var spread = layout.RequiredPixels(PagePart, 10, null)!;

            var check = await new OrderFileCheckService(new StubCatalog(layout), new StubInspector(spread))
                .CheckAsync(new[]
                {
                    GroupWithCover(new Dictionary<long, long> { [26] = 99 }, PageFile("1.jpg")),
                });

            var alert = Assert.Single(check.Books[0].Alerts);
            Assert.Equal(FileAlertLevel.Information, alert.Level);
            Assert.StartsWith("Книга 1", alert.Message, StringComparison.Ordinal);
            Assert.Contains("размер обложки", alert.Message, StringComparison.Ordinal);
            Assert.Equal(0, check.BlockingCount);
            Assert.True(check.CanSend);
        }

        private static OrderFileCheckService Service(SiteLayout layout, PixelSize size) =>
            new(new StubCatalog(layout), new StubInspector(size));

        private static OrderFileCheckService ServiceFor(
            Func<int, SiteLayout?> byCall,
            PixelSize size) =>
            new(new StubCatalog(null, byCall), new StubInspector(size));

        /// <summary>A book whose page part is 410×290 mm at 300 dpi with ten spreads.</summary>
        private static SiteLayout SpreadLayout(double widthMm, double heightMm)
        {
            var page = new PartLayout(
                PagePart,
                "Разворот",
                Array.Empty<PageSizeRange>(),
                new PageSize(widthMm, heightMm),
                PrintMargins.None,
                Array.Empty<SizeIndexCondition>(),
                PrintMargins.None,
                0);

            return new SiteLayout(Product, Layflat, 300, new[] { page }, 37556);
        }

        /// <summary>A layout with a cover part of its own and the spread part, as the site sends it.</summary>
        private static SiteLayout LayoutWithCover(double coverWidthMm, double coverHeightMm) =>
            new(
                Product,
                Layflat,
                300,
                new[]
                {
                    new PartLayout(
                        CoverPart,
                        "Обложка",
                        Array.Empty<PageSizeRange>(),
                        new PageSize(coverWidthMm, coverHeightMm),
                        PrintMargins.None,
                        Array.Empty<SizeIndexCondition>(),
                        PrintMargins.None,
                        0),
                    new PartLayout(
                        PagePart,
                        "Разворот",
                        Array.Empty<PageSizeRange>(),
                        new PageSize(410, 290),
                        PrintMargins.None,
                        Array.Empty<SizeIndexCondition>(),
                        PrintMargins.None,
                        0),
                },
                37556);

        /// <summary>A cover part whose size row is chosen by the spread count, as the site sends it.</summary>
        private static SiteLayout IndexedCoverLayout()
        {
            var bleeds = new PrintMargins(18, 17, 18, 17);
            var cover = new PartLayout(
                CoverPart,
                "Обложка",
                new[]
                {
                    new PageSizeRange(8, new PageSize(428, 296), bleeds),
                    new PageSizeRange(10, new PageSize(430, 296), bleeds),
                },
                null,
                bleeds,
                new[]
                {
                    new SizeIndexCondition("26", "4", new[] { new SizeIndexRule(8, 10, 10, PagePart) }),
                },
                PrintMargins.None,
                5);

            return new SiteLayout(
                Product,
                Layflat,
                300,
                new[]
                {
                    cover,
                    new PartLayout(
                        PagePart,
                        "Разворот",
                        Array.Empty<PageSizeRange>(),
                        new PageSize(410, 290),
                        PrintMargins.None,
                        Array.Empty<SizeIndexCondition>(),
                        PrintMargins.None,
                        0),
                },
                37556);
        }

        private static UploadGroup GroupWithCover(params PageFile[] pages) =>
            GroupWithCover(null, pages);

        /// <summary>
        /// A book whose cover size is named by a question asked on the spreads part, which is how
        /// the site asks it: the interlayer of the spreads decides how wide the cover is, and the
        /// cover part itself only answers about its lamination.
        /// </summary>
        private static UploadGroup GroupWithCover(
            IReadOnlyDictionary<long, long>? pageOptions,
            params PageFile[] pages)
        {
            var cover = PageFile("cover.jpg");
            var parts = new List<SelectedPart>
            {
                new(PagePart, new PartSelection(pageOptions ?? new Dictionary<long, long>(), 10)),
                new(CoverPart, new PartSelection(new Dictionary<long, long> { [65075] = 417892 }, 1)),
            };

            var book = new Book(
                new SiteProduct(Product, "layflat", "Лайфлат", "photo-book", new List<ProductVariant>()),
                new ProductVariant(Layflat, "21x30", new List<ProductOption>(), new List<ProductPart>()),
                PagePartId: PagePart,
                parts,
                pages,
                CoverPartId: CoverPart,
                CoverFile: cover);

            return new UploadGroup(
                1,
                new OrderGroup(SiteRegion.Com, new[] { book }),
                new CreateProjectRequest(Product, Layflat, 37556),
                new[] { cover }.Concat(pages).ToArray());
        }

        private static UploadGroup Group(params PageFile[] pages) => GroupOf(BookOf(pages));

        /// <summary>One project with several books in it, as the plan sends them.</summary>
        private static UploadGroup GroupOf(params Book[] books) =>
            new(
                1,
                new OrderGroup(SiteRegion.Com, books),
                new CreateProjectRequest(Product, Layflat, 37556),
                books.SelectMany(book => book.AllFiles).ToList());

        private static Book BookOf(params PageFile[] pages) =>
            new(
                new SiteProduct(Product, "layflat", "Лайфлат", "photo-book", new List<ProductVariant>()),
                new ProductVariant(Layflat, "21x30", new List<ProductOption>(), new List<ProductPart>()),
                PagePartId: PagePart,
                new List<SelectedPart> { new(PagePart, new PartSelection(new Dictionary<long, long>(), 10)) },
                pages);

        private static PageFile PageFile(string name) => new(@"C:\photos\" + name, 1024);

        private sealed class StubCatalog : ISiteCatalogService
        {
            private readonly SiteLayout? _layout;
            private readonly Func<int, SiteLayout?>? _byCall;

            public StubCatalog(SiteLayout? layout, Func<int, SiteLayout?>? byCall = null)
            {
                _layout = layout;
                _byCall = byCall;
            }

            public int Calls { get; private set; }

            public Task<Result<SiteLayout>> GetLayoutAsync(
                SiteRegion region,
                long productId,
                long componentId,
                CancellationToken cancellationToken = default)
            {
                Calls++;

                var layout = _byCall is null ? _layout : _byCall(Calls);

                return Task.FromResult(layout is null
                    ? Result<SiteLayout>.Fail("Сайт не назвал геометрию.")
                    : Result<SiteLayout>.Ok(layout));
            }

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

        private sealed class StubInspector : IBookFileInspector
        {
            private readonly PixelSize _size;
            private readonly IReadOnlyDictionary<string, PixelSize>? _byName;

            public StubInspector(PixelSize size, IReadOnlyDictionary<string, PixelSize>? byName = null)
            {
                _size = size;
                _byName = byName;
            }

            public int Calls { get; private set; }

            public Task<IReadOnlyList<PageFileFacts>> InspectAsync(
                IReadOnlyList<PageFile> files,
                CancellationToken cancellationToken = default)
            {
                Calls++;

                var facts = new List<PageFileFacts>(files.Count);
                foreach (var file in files)
                {
                    var name = System.IO.Path.GetFileName(file.Path);
                    var size = _byName is not null && _byName.TryGetValue(name, out var byName) ? byName : _size;
                    facts.Add(new PageFileFacts(file.Path, Exists: true, size));
                }

                return Task.FromResult<IReadOnlyList<PageFileFacts>>(facts);
            }
        }

        /// <summary>Progress that runs as it is reported, so the order of reports is the order seen.</summary>
        private sealed class ImmediateProgress : IProgress<FileCheckProgress>
        {
            private readonly List<FileCheckProgress> _reports;

            public ImmediateProgress(List<FileCheckProgress> reports) => _reports = reports;

            public void Report(FileCheckProgress value) => _reports.Add(value);
        }
    }
}
