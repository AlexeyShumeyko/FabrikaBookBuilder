using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
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
            Assert.Equal("Продолжить загрузку", screen.ButtonText);

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
            int booksPerGroup = 1)
        {
            var screen = new SiteUploadViewModel(creation, upload, check);
            var books = Enumerable.Range(0, groups)
                .Select(_ => Enumerable.Range(0, booksPerGroup).Select(_ => Book()).ToList())
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
                    Enumerable.Range(0, pages).Select(page => new PageFile($@"C:\p{index}-{page}.jpg", 1024)).ToList()))
                .ToList();

            screen.Prepare(Session, summary, groupsToSend);
            return screen;
        }

        private static Book Book() =>
            new(
                new SiteProduct(4494, "layflat", "Лайфлат", "photo-book", new List<ProductVariant>()),
                new ProductVariant(9255, "21x30", new List<ProductOption>(), new List<ProductPart>()),
                PagePartId: 94,
                new List<SelectedPart> { new(94, new PartSelection(new Dictionary<long, long>(), 10)) },
                new List<PageFile>());

        private sealed class StubCreation : IProjectCreationService
        {
            private readonly long[] _ids;

            internal StubCreation(params long[] ids) => _ids = ids;

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
                        null))
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
