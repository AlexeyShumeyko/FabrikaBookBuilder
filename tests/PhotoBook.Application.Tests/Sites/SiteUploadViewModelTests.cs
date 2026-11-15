using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Files;
using PhotoBook.Application.Sites.Catalog.Layout;
using PhotoBook.Application.Sites.Books.Grouping;
using PhotoBook.Application.Sites.Books.Pricing;
using PhotoBook.Application.Sites.Books.Submission;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Application.Sites.Completion;
using PhotoBook.Application.Sites.Pricing;
using PhotoBook.Application.Sites.Projects;
using PhotoBook.Application.Sites.Upload;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Application.Tests.Sites
{
    /// <summary>
    /// The screen is the only place that starts a write, so what it does with a refusal, a lost
    /// connection and a difference decides whether the customer's book ends up whole.
    /// </summary>
    public class SiteUploadViewModelTests
    {
        private static readonly SiteSession Session =
            new(SiteRegion.Com, "token-value", new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

        [Fact]
        public async Task SendsEveryGroupAndShowsWhereTheProjectIs()
        {
            var creation = new StubCreation(1001, 1002);
            var upload = new StubUpload((session, projectId, pages, _, _, _) =>
                Task.FromResult(Accepted(projectId, pages.Count)));
            var check = new StubCheck(noDifferences: true);

            var screen = Screen(creation, upload, check, groups: 2, pages: 3);

            Assert.True(screen.CanUpload);
            Assert.Equal(6, screen.Total);

            await screen.UploadAsync();

            Assert.Equal(UploadStep.Done, screen.Step);
            Assert.Equal(6, screen.Uploaded);
            Assert.Equal(2, screen.Results.Count);
            Assert.All(screen.Results, result => Assert.True(result.IsComplete));
            Assert.True(screen.HasUrl);
            Assert.Equal("https://fabrika-fotoknigi.com/layflat?projectId=1001", screen.Url);
            Assert.Single(creation.Asked);
            Assert.Equal(2, creation.Asked[0].Count);
            Assert.Equal(2, upload.Calls.Count);
        }

        [Fact]
        public void ABookLeftOutByTheOrderKeepsTheButtonOff()
        {
            var screen = Screen(new StubCreation(1001), new StubUpload((session, projectId, pages, _, _, _) =>
                Task.FromResult(Accepted(projectId, pages.Count))), new StubCheck(true), groups: 1, pages: 3);

            // The order page names every book it could not price; one of those is enough to
            // stop the run, because a project nobody priced is a project nobody confirmed.
            screen.Prepare(Session, screen.Summary!, Array.Empty<UploadGroup>(), new[] { "Книга 2: цена не получена." });

            Assert.False(screen.CanUpload);
            Assert.Contains("цена не получена", screen.Message);
        }

        [Fact]
        public async Task AnExpiredSessionAsksForThePasswordAndStopsTheRun()
        {
            var upload = new StubUpload((session, projectId, pages, _, _, _) => Task.FromResult(
                new BookUploadResult(
                    UploadState.Failed,
                    new UploadSession(SiteRegion.Com, projectId, pages.Count, 0),
                    "Нужно войти заново: сеанс истёк.",
                    UploadFailureKind.Unauthorized)));

            var screen = Screen(new StubCreation(1001), upload, new StubCheck(true), groups: 1, pages: 3);

            var asked = false;
            screen.NeedsSignIn += (_, _) => asked = true;

            await screen.UploadAsync();

            Assert.True(asked);
            Assert.Equal(UploadStep.Stopped, screen.Step);
            Assert.False(screen.Results[0].IsComplete);
            Assert.Contains("войти заново", screen.Message);
        }

        [Fact]
        public async Task ALostConnectionLeavesTheButtonAbleToContinue()
        {
            var first = true;
            var upload = new StubUpload((session, projectId, pages, _, _, _) =>
            {
                if (!first)
                    return Task.FromResult(Accepted(projectId, pages.Count));

                first = false;

                return Task.FromResult(new BookUploadResult(
                    UploadState.Failed,
                    new UploadSession(SiteRegion.Com, projectId, pages.Count, 1),
                    "Нет связи с сайтом. Проверьте интернет и попробуйте снова.",
                    UploadFailureKind.ConnectionLost));
            });

            var screen = Screen(new StubCreation(1001), upload, new StubCheck(true), groups: 1, pages: 3);

            await screen.UploadAsync();

            Assert.Equal(UploadStep.Stopped, screen.Step);
            Assert.Equal(1, screen.Uploaded);
            Assert.True(screen.CanUpload);
            Assert.Equal(UploadStep.Stopped, screen.Step);

            await screen.UploadAsync();

            Assert.Equal(UploadStep.Done, screen.Step);
            Assert.Equal(2, upload.Calls.Count);
        }

        [Fact]
        public async Task AGroupWithSeveralBooksIsReportedInsteadOfSent()
        {
            var creation = new StubCreation(1001);

            var screen = Screen(creation, new StubUpload(), new StubCheck(true), groups: 1, pages: 3, booksPerGroup: 2);

            Assert.False(screen.CanUpload);
            Assert.Contains("несколько книг", screen.Message);

            await screen.UploadAsync();

            Assert.Empty(creation.Asked);
        }

        [Fact]
        public async Task WhatTheSiteHoldsDifferentlyIsShownRatherThanCalledSuccess()
        {
            var check = new StubCheck(noDifferences: false);

            var screen = Screen(new StubCreation(1001), new StubUpload((s, id, pages, _, _, _) =>
                Task.FromResult(Accepted(id, pages.Count))), check, groups: 1, pages: 3);

            await screen.UploadAsync();

            Assert.Equal(UploadStep.Stopped, screen.Step);
            Assert.False(screen.Results[0].IsComplete);
            Assert.Contains("не всё", screen.Message);
            Assert.False(string.IsNullOrWhiteSpace(screen.Results[0].Problems[0]));
        }

        [Fact]
        public async Task CancellingKeepsWhatTheSiteAlreadyHas()
        {
            var upload = new StubUpload((session, projectId, pages, _, _, _) => Task.FromResult(
                new BookUploadResult(
                    UploadState.Cancelled,
                    new UploadSession(SiteRegion.Com, projectId, pages.Count, 1),
                    "Загрузка отменена.")));

            var screen = Screen(new StubCreation(1001), upload, new StubCheck(true), groups: 1, pages: 3);

            await screen.UploadAsync();

            Assert.Equal(UploadStep.Stopped, screen.Step);
            Assert.Equal(1, screen.Uploaded);
            Assert.Contains("остановлена", screen.Message);
        }

        private static BookUploadResult Accepted(long projectId, int pages) =>
            new(
                UploadState.Completed,
                new UploadSession(SiteRegion.Com, projectId, pages, pages),
                null);

        private static SiteUploadViewModel Screen(
            StubCreation creation,
            StubUpload upload,
            StubCheck check,
            int groups,
            int pages,
            int booksPerGroup = 1,
            PixelSize? size = null)
        {
            var screen = new SiteUploadViewModel(creation, upload, check, Files(size));
            var pagesOfGroup = Enumerable.Range(0, groups)
                .Select(index => Enumerable.Range(0, pages)
                    .Select(page => new PageFile($@"C:\p{index}-{page}.jpg", 1024))
                    .ToList())
                .ToList();

            var books = pagesOfGroup
                .Select(groupPages => Enumerable.Range(0, booksPerGroup)
                    .Select(_ => Book(groupPages))
                    .ToList())
                .ToList();

            var layout = BookGrouping.FromManual(SiteRegion.Com, books);
            var quotes = Enumerable.Range(0, groups)
                .Select(_ => new GroupQuote(new List<BookQuote>
                {
                    new(1, Book(), SitePriceCheck.Valid(new SitePriceQuote(542m, "x", "RUB")))
                }))
                .ToList();

            var summary = SubmissionSummary.Build(layout, quotes);
            var groupsToSend = layout.Groups
                .Select((group, index) => new UploadGroup(
                    index + 1,
                    group,
                    new CreateProjectRequest(4494, 9255, 37556),
                    pagesOfGroup[index]))
                .ToList();

            screen.Prepare(Session, summary, groupsToSend);
            screen.CheckFilesAsync().GetAwaiter().GetResult();
            return screen;
        }

        [Fact]
        public async Task ARunThatMeetsAnExpiredTokenAsksForAPasswordAndCarriesOn()
        {
            var creation = new StubCreation(1001);
            var upload = new StubUpload((session, projectId, pages, _, _, _) =>
                Task.FromResult(new BookUploadResult(
                    UploadState.Failed,
                    new UploadSession(SiteRegion.Com, projectId, pages.Count, 0),
                    "Нужно войти заново: сеанс истёк.",
                    UploadFailureKind.Unauthorized)));
            var screen = Screen(creation, upload, new StubCheck(true), groups: 1, pages: 2);

            var asked = 0;
            screen.NeedsSignIn += (_, _) => asked++;

            await screen.UploadAsync();

            Assert.Equal(1, asked);
            Assert.False(screen.CanUpload);

            // What the shell does after the customer signs in: the same screen keeps its plan
            // and its price, so continuing is one press rather than a new order.
            screen.RefreshSession(Session);

            Assert.True(screen.CanUpload);
            Assert.Equal(UploadStep.Stopped, screen.Step);
        }

        [Fact]
        public async Task OffersTheProjectOnlyAfterTheSiteNamedIt()
        {
            var screen = Screen(
                new StubCreation(1001),
                new StubUpload((session, projectId, pages, _, _, _) =>
                    Task.FromResult(Accepted(projectId, pages.Count))),
                new StubCheck(true),
                groups: 1,
                pages: 2);

            Assert.False(screen.CanOpenSite);

            await screen.UploadAsync();

            Assert.True(screen.CanOpenSite);
            Assert.Equal(screen.Url, screen.OnSiteLink);

            screen.ReportLinkFailure();

            Assert.Contains(screen.OnSiteLink, screen.Message);
        }

        [Fact]
        public async Task StopsBeforeSendingPagesIntoAProjectWithoutItsParameters()
        {
            var upload = new StubUpload();
            var screen = Screen(
                new StubCreation(1001) { Error = "Проект создан, но сайт не принял его параметры и цену: отказ." },
                upload,
                new StubCheck(true),
                groups: 1,
                pages: 2);

            await screen.UploadAsync();

            Assert.Empty(upload.Calls);
            Assert.Contains("параметры", screen.Message, StringComparison.Ordinal);
            Assert.NotEqual(UploadStep.Done, screen.Step);
        }

        [Fact]
        public void APageTooSmallKeepsTheButtonOffAndSaysWhy()
        {
            var screen = Screen(
                new StubCreation(1001),
                new StubUpload((session, projectId, pages, _, _, _) =>
                    Task.FromResult(Accepted(projectId, pages.Count))),
                new StubCheck(true),
                groups: 1,
                pages: 2,
                size: new PixelSize(800, 600));

            Assert.False(screen.CanUpload);
            Assert.True(screen.HasFileProblems);
            Assert.Contains(screen.FileLines, line => line.Contains("Страница 1:"));
            Assert.Contains("исправьте", screen.FilesText);
        }

        [Fact]
        public void AWiderPageAsksAndTheButtonWaitsForTheAnswer()
        {
            var screen = Screen(
                new StubCreation(1001),
                new StubUpload((session, projectId, pages, _, _, _) =>
                    Task.FromResult(Accepted(projectId, pages.Count))),
                new StubCheck(true),
                groups: 1,
                pages: 1,
                size: new PixelSize(5000, 3500));

            Assert.True(screen.NeedsConfirmation);
            Assert.False(screen.CanUpload);

            screen.ConfirmationsAccepted = true;

            Assert.True(screen.CanUpload);
        }

        [Fact]
        public void ReadFilesThatFitSaySoAndTheButtonComesOn()
        {
            var screen = Screen(
                new StubCreation(1001),
                new StubUpload((session, projectId, pages, _, _, _) =>
                    Task.FromResult(Accepted(projectId, pages.Count))),
                new StubCheck(true),
                groups: 1,
                pages: 2);

            Assert.True(screen.CanUpload);
            Assert.False(screen.NeedsConfirmation);
            Assert.Contains("всё совпадает", screen.FilesText);
            Assert.Single(screen.FileLines);
        }

        [Fact]
        public async Task TheCheckForgetsTheOldYesWhenTheFilesAreReadAgain()
        {
            var screen = Screen(
                new StubCreation(1001),
                new StubUpload((session, projectId, pages, _, _, _) =>
                    Task.FromResult(Accepted(projectId, pages.Count))),
                new StubCheck(true),
                groups: 1,
                pages: 1,
                size: new PixelSize(5000, 3500));

            screen.ConfirmationsAccepted = true;
            await screen.CheckFilesAsync();

            Assert.False(screen.ConfirmationsAccepted);
            Assert.False(screen.CanUpload);
        }

        /// <summary>The file check over a page of the given pixel size.</summary>
        private static OrderFileCheckService Files(PixelSize? size = null)
        {
            var layout = new SiteLayout(
                4494,
                9255,
                300,
                new[]
                {
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

            var required = layout.RequiredPixels(94, 10, null)!;

            return new OrderFileCheckService(
                new FileCatalog(layout),
                new FileInspector(size ?? required));
        }

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
            private readonly PixelSize _size;

            internal FileInspector(PixelSize size) => _size = size;

            public Task<IReadOnlyList<PageFileFacts>> InspectAsync(
                IReadOnlyList<PageFile> files,
                CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<PageFileFacts>>(
                    files.Select(file => new PageFileFacts(file.Path, true, _size)).ToList());
        }

        private static Book Book(IReadOnlyList<PageFile>? files = null) =>
            new(
                new SiteProduct(4494, "layflat", "Лайфлат", "photo-book", new List<ProductVariant>()),
                new ProductVariant(9255, "21x30", new List<ProductOption>(), new List<ProductPart>()),
                PagePartId: 94,
                new List<SelectedPart> { new(94, new PartSelection(new Dictionary<long, long>(), 10)) },
                files ?? new List<PageFile>());

        private sealed class StubCreation : IProjectCreationService
        {
            private readonly long[] _ids;

            internal StubCreation(params long[] ids) => _ids = ids;

            internal string? Error { get; init; }

            internal List<IReadOnlyList<GroupRequest>> Asked { get; } = new();

            public Task<ProjectCreationPlan> CreateAsync(
                SiteSession session,
                IReadOnlyList<GroupRequest> groups,
                CancellationToken cancellationToken = default)
            {
                Asked.Add(groups);

                var created = groups
                    .Select((group, index) => new GroupCreation(
                        index + 1,
                        group.Group,
                        GroupCreationState.Created,
                        _ids[Math.Min(index, _ids.Length - 1)],
                        Error))
                    .ToList();

                return Task.FromResult(new ProjectCreationPlan(created));
            }
        }

        private sealed class StubUpload : IBookUploadService
        {
            private readonly Func<SiteSession, long, IReadOnlyList<PageFile>, int?, IProgress<UploadProgress>?, CancellationToken, Task<BookUploadResult>> _answer;

            internal StubUpload()
                : this((session, projectId, pages, _, _, _) =>
                    Task.FromResult(new BookUploadResult(
                        UploadState.Completed,
                        new UploadSession(SiteRegion.Com, projectId, pages.Count, pages.Count),
                        null)))
            {
            }

            internal StubUpload(
                Func<SiteSession, long, IReadOnlyList<PageFile>, int?, IProgress<UploadProgress>?, CancellationToken, Task<BookUploadResult>> answer) =>
                _answer = answer;

            internal List<long> Calls { get; } = new();

            public Task<BookUploadResult> UploadAsync(
                SiteSession session,
                long projectId,
                IReadOnlyList<PageFile> files,
                int? alreadyUploaded = null,
                IProgress<UploadProgress>? progress = null,
                CancellationToken cancellationToken = default)
            {
                Calls.Add(projectId);
                return _answer(session, projectId, files, alreadyUploaded, progress, cancellationToken);
            }
        }

        private sealed class StubCheck : IProjectCheckService
        {
            private readonly bool _noDifferences;

            internal StubCheck(bool noDifferences) => _noDifferences = noDifferences;

            public Task<Result<ProjectCheck>> CheckAsync(
                SiteSession session,
                long projectId,
                ProjectExpectation expected,
                CancellationToken cancellationToken = default)
            {
                var project = new SiteProjectSnapshot(
                    projectId,
                    4494,
                    string.Empty,
                    "/layflat",
                    true,
                    Array.Empty<SiteProjectItem>());

                var differences = _noDifferences
                    ? Array.Empty<string>()
                    : new[] { "Книга 1: на сайте 2 страниц, отправляли 3." };

                return Task.FromResult(Result<ProjectCheck>.Ok(
                    new ProjectCheck(project, "https://fabrika-fotoknigi.com/layflat?projectId=" + projectId, differences)));
            }
        }
    }
}
