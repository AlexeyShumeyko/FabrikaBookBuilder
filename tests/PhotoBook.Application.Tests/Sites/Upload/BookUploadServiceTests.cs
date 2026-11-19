using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Upload;

namespace PhotoBook.Application.Tests.Sites.Upload
{
    /// <summary>
    /// The roadmap's criterion lives here: three books of ten spreads go up in order, the
    /// progress matches what the site holds, and a stopped upload resumes instead of restarting.
    /// </summary>
    public class BookUploadServiceTests
    {
        private static readonly SiteSession Session =
            new(SiteRegion.Com, "token-value", new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

        private static List<PageFile> Pages(int count) =>
            Enumerable.Range(1, count)
                .Select(number => new PageFile($@"C:\photos\{number}.jpg", 1024))
                .ToList();

        [Fact]
        public async Task SendsEveryPageInOrderFromPositionZero()
        {
            var upload = new StubUpload();
            var pages = Pages(10);

            var result = await new BookUploadService(upload)
                .UploadAsync(Session, 527733, pages);

            Assert.Equal(UploadState.Completed, result.State);
            Assert.True(result.IsComplete);
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, upload.Positions);
            Assert.Equal(pages.Select(page => page.Path), upload.Paths);
            Assert.Equal(10, result.Session.FilesUploaded);
            Assert.Equal(10, result.Session.FilesTotal);
            Assert.Equal(527733, result.Session.SiteProjectId);
            Assert.Equal(SiteRegion.Com, result.Session.Region);
        }

        [Fact]
        public async Task SendsEveryPageAgainstTheBookTheProjectHolds()
        {
            var upload = new StubUpload();

            var result = await new BookUploadService(upload)
                .UploadAsync(Session, 527733, Pages(3), projectItemId: 65075);

            Assert.True(result.IsComplete);
            Assert.All(upload.ItemIds, id => Assert.Equal(65075, id));
        }

        [Fact]
        public async Task StartsAfterThePagesTheSiteAlreadyHas()
        {
            var upload = new StubUpload();

            var result = await new BookUploadService(upload)
                .UploadAsync(Session, 527733, Pages(10), alreadyUploaded: 7);

            Assert.Equal(new[] { 7, 8, 9 }, upload.Positions);
            Assert.Equal(10, result.Session.FilesUploaded);
            Assert.True(result.IsComplete);
        }

        [Fact]
        public async Task ContinuesFromTheRecordAfterARestart()
        {
            var upload = new StubUpload();
            var record = new StubAccepted(start: 6);

            var result = await new BookUploadService(upload, record)
                .UploadAsync(Session, 527733, Pages(10));

            Assert.Equal(new[] { 6, 7, 8, 9 }, upload.Positions);
            Assert.True(result.IsComplete);
            Assert.Equal(10, result.Session.FilesUploaded);
        }

        [Fact]
        public async Task StopsAtTheFirstRefusalAndSaysWhereToResume()
        {
            var upload = new StubUpload(failAt: 3);
            var record = new StubAccepted();

            var result = await new BookUploadService(upload, record)
                .UploadAsync(Session, 527733, Pages(10));

            Assert.Equal(UploadState.Failed, result.State);
            Assert.False(result.IsComplete);
            Assert.Equal(3, result.Session.FilesUploaded);
            Assert.Equal(7, result.Session.Remaining);
            Assert.Equal("Сайт не принял файл: страница 4.", result.Error);
            Assert.Equal(UploadFailureKind.File, result.Kind);
            Assert.False(result.CanResume);
            // The window had one page in flight when the refusal came back, so four positions
            // went out in all; nothing after that one was started.
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, upload.Positions);

            // The record holds what the site took, so the next run starts at the failed page.
            Assert.Equal(3, record.Written[record.Written.Count - 1]);
            Assert.Equal(0, record.Forgotten);
        }

        [Fact]
        public async Task ReportsProgressAfterEveryAcceptedFile()
        {
            var upload = new StubUpload();
            var reports = new List<UploadProgress>();

            await new BookUploadService(upload).UploadAsync(
                Session,
                527733,
                Pages(3),
                progress: new Immediate(reports));

            // One report as a file starts and one as it is accepted: the bar moves by files the
            // site actually took, not by files the program hoped to send.
            Assert.Equal(6, reports.Count);
            Assert.Equal(0, reports[0].FilesUploaded);
            Assert.EndsWith("1.jpg", reports[0].CurrentFile);
            Assert.Contains(reports, report => report.CurrentFile.EndsWith("2.jpg"));
            Assert.Equal(3, reports[reports.Count - 1].FilesUploaded);
            Assert.Equal(string.Empty, reports[reports.Count - 1].CurrentFile);
        }

        /// <summary>
        /// Two pages travel at once: the site orders them by the position each request names, and
        /// waiting for one store before starting the next only made the run as slow as the site.
        /// </summary>
        [Fact]
        public async Task KeepsASecondPageInFlightWithoutLosingTheOrder()
        {
            var upload = new SlowUpload(TimeSpan.FromMilliseconds(15));

            var result = await new BookUploadService(upload)
                .UploadAsync(Session, 527733, Pages(6));

            Assert.True(result.IsComplete);
            Assert.Equal(2, upload.Peak);
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, upload.Positions);
        }

        /// <summary>
        /// A refusal is reported with the resume point before it, and a page that arrived while
        /// the refused one was in flight does not move that point past the gap.
        /// </summary>
        [Fact]
        public async Task ARefusalLeavesTheResumePointAtTheGap()
        {
            var upload = new GatedUpload(failAt: 1);
            var record = new StubAccepted();

            var result = await new BookUploadService(upload, record)
                .UploadAsync(Session, 527733, Pages(6));

            Assert.Equal(UploadState.Failed, result.State);

            // The page after the refused one had already gone out, so the card may not read
            // "two pages on the site" when the second is the one that failed.
            Assert.Equal(new[] { 0, 1, 2 }, upload.Positions.Take(3));
            Assert.Equal(1, result.Session.FilesUploaded);
            Assert.Equal(1, record.Written[record.Written.Count - 1]);
        }

        [Fact]
        public async Task StopsWhenCancelledAndKeepsWhatWasSent()
        {
            using var cancellation = new CancellationTokenSource();
            var upload = new StubUpload(cancelAfter: cancellation);
            var record = new StubAccepted();

            var result = await new BookUploadService(upload, record).UploadAsync(
                Session,
                527733,
                Pages(10),
                cancellationToken: cancellation.Token);

            Assert.Equal(UploadState.Cancelled, result.State);
            Assert.True(result.WasCancelled);
            Assert.Equal(1, result.Session.FilesUploaded);
            Assert.Equal(9, result.Session.Remaining);
            Assert.Single(upload.Positions);
            Assert.Equal(1, record.Written[record.Written.Count - 1]);
        }

        [Fact]
        public async Task ForgetsTheRecordWhenTheBookIsComplete()
        {
            var record = new StubAccepted();

            await new BookUploadService(new StubUpload(), record)
                .UploadAsync(Session, 527733, Pages(3));

            Assert.Equal(3, record.Written[record.Written.Count - 1]);
            Assert.Equal(1, record.Forgotten);
        }

        [Fact]
        public async Task WarnsWhenTheRecordCannotBeSavedButTheBookStillGoesUp()
        {
            var record = new StubAccepted(failWrites: true);

            var result = await new BookUploadService(new StubUpload(), record)
                .UploadAsync(Session, 527733, Pages(3));

            Assert.True(result.IsComplete);
            Assert.Null(result.Error);
            Assert.False(string.IsNullOrWhiteSpace(result.Notice));
        }

        [Fact]
        public async Task AnExpiredSessionIsReportedSoTheCustomerCanSignInAgain()
        {
            var upload = new StubUpload(
                failAt: 0,
                refusal: UploadFailureKind.Unauthorized,
                error: "Нужно войти заново: сеанс истёк.");

            var result = await new BookUploadService(upload)
                .UploadAsync(Session, 527733, Pages(3));

            Assert.Equal(UploadState.Failed, result.State);
            Assert.True(result.NeedsSignIn);
            Assert.False(result.CanResume);
            Assert.Equal(0, result.Session.FilesUploaded);
        }

        [Fact]
        public async Task ALostConnectionIsReportedAsResumable()
        {
            var upload = new StubUpload(
                failAt: 2,
                refusal: UploadFailureKind.ConnectionLost,
                error: "Нет связи с сайтом. Проверьте интернет и попробуйте снова.");

            var result = await new BookUploadService(upload)
                .UploadAsync(Session, 527733, Pages(5));

            Assert.True(result.CanResume);
            Assert.False(result.NeedsSignIn);
            Assert.Equal(2, result.Session.FilesUploaded);
        }

        /// <summary>
        /// The same photograph in every book is sent once. The site names the picture it stored,
        /// and the other books ask for a copy of it - the request the site's own uploader makes,
        /// and the reason thirty books of ten spreads cost ten uploads rather than three hundred.
        /// </summary>
        [Fact]
        public async Task ARepeatedPhotographIsCopiedInsteadOfSentAgain()
        {
            var upload = new StubUpload();
            var images = new UploadedImages();
            images.Remember(@"C:\photos\shared.jpg", 900);

            var pages = new List<PageFile>
            {
                new(@"C:\photos\shared.jpg", 1024),
                new(@"C:\photos\own.jpg", 1024),
                new(@"C:\photos\shared.jpg", 1024)
            };

            var result = await new BookUploadService(upload)
                .UploadAsync(Session, 527733, pages, images: images);

            Assert.True(result.IsComplete);

            // Only the picture the project did not hold travelled as a file.
            Assert.Equal(new[] { 1 }, upload.Positions);
            Assert.Equal(new[] { 0, 2 }, upload.Copies.Select(copy => copy.Position));
            Assert.All(upload.Copies, copy => Assert.Equal(900, copy.ImageId));
        }

        [Fact]
        public async Task RemembersThePictureTheSiteNamedForTheNextBook()
        {
            var upload = new StubUpload { NamedImageId = 18954166 };
            var images = new UploadedImages();

            await new BookUploadService(upload).UploadAsync(
                Session, 527733, Pages(2), projectItemId: 65075, images: images);

            Assert.True(images.TryGet(@"C:\photos\1.jpg", out var id));
            Assert.Equal(18954166, id);
        }

        /// <summary>
        /// A copy the site refuses is not a lost page: the file itself goes, and the rest of the
        /// book stops asking for copies rather than paying a refused request per page.
        /// </summary>
        [Fact]
        public async Task FallsBackToSendingTheFileWhenTheCopyIsRefused()
        {
            var upload = new StubUpload { RefuseCopies = true };
            var images = new UploadedImages();
            images.Remember(@"C:\photos\shared.jpg", 900);

            var pages = new List<PageFile>
            {
                new(@"C:\photos\shared.jpg", 1024),
                new(@"C:\photos\shared.jpg", 1024)
            };

            var result = await new BookUploadService(upload)
                .UploadAsync(Session, 527733, pages, images: images);

            Assert.True(result.IsComplete);
            Assert.Single(upload.Copies);
            Assert.Equal(new[] { 0, 1 }, upload.Positions);
        }

        [Fact]
        public async Task ABookWithoutPagesIsAlreadyDone()
        {
            var upload = new StubUpload();

            var result = await new BookUploadService(upload)
                .UploadAsync(Session, 527733, Pages(0));

            Assert.True(result.IsComplete);
            Assert.Empty(upload.Positions);
            Assert.Equal(0, result.Session.FilesTotal);
        }

        [Fact]
        public async Task ARefusedPageIsWrittenDownWithTheProjectPositionAndFile()
        {
            var journal = new RecordingJournal();
            var upload = new StubUpload(failAt: 3, error: "Сайт не принял файл: страница 4.");

            await new BookUploadService(upload, journal: journal)
                .UploadAsync(Session, 527733, Pages(6));

            var entry = Assert.Single(journal.Entries);
            Assert.Equal(JournalLevel.Warning, entry.Level);
            Assert.Equal("Отправка на сайт", entry.Source);
            Assert.Contains("527733", entry.Message, StringComparison.Ordinal);
            Assert.Contains("4 из 6", entry.Message, StringComparison.Ordinal);
            Assert.Contains("4.jpg", entry.Message, StringComparison.Ordinal);
            Assert.Contains("страница 4", entry.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AFinishedBookIsWrittenDown()
        {
            var journal = new RecordingJournal();

            await new BookUploadService(new StubUpload(), journal: journal)
                .UploadAsync(Session, 527733, Pages(2));

            var entry = Assert.Single(journal.Entries);
            Assert.Equal(JournalLevel.Information, entry.Level);
            Assert.Contains("527733", entry.Message, StringComparison.Ordinal);
            Assert.Contains("файлов 2", entry.Message, StringComparison.Ordinal);
        }

        private sealed class Immediate : IProgress<UploadProgress>
        {
            private readonly List<UploadProgress> _reports;

            internal Immediate(List<UploadProgress> reports) => _reports = reports;

            public void Report(UploadProgress value) => _reports.Add(value);
        }

        private sealed class StubAccepted : IAcceptedFilesStore
        {
            private readonly int _start;
            private readonly bool _failWrites;

            internal StubAccepted(int start = 0, bool failWrites = false)
            {
                _start = start;
                _failWrites = failWrites;
            }

            internal List<int> Written { get; } = new();

            internal int Forgotten { get; private set; }

            public int Accepted(SiteRegion region, long projectId, long itemId, int filesTotal) => _start;

            public Result Remember(UploadSession session)
            {
                if (_failWrites)
                    return Result.Fail("Диск недоступен.");

                Written.Add(session.FilesUploaded);
                return Result.Ok();
            }

            public Result Forget(SiteRegion region, long projectId, long itemId)
            {
                Forgotten++;
                return Result.Ok();
            }
        }

        /// <summary>An uploader slow enough for two sends to overlap, counting how many do.</summary>
        private sealed class SlowUpload : ISiteFileUploadService
        {
            public Task<PageUpload> CopyAsync(
                SiteSession session,
                long projectId,
                long projectItemId,
                int position,
                long imageId,
                string fileName,
                int width = 0,
                int height = 0,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(PageUpload.AcceptedImage(imageId));
            private readonly object _gate = new();
            private readonly TimeSpan _delay;
            private readonly int _failAt;
            private int _inFlight;

            internal SlowUpload(TimeSpan delay, int failAt = -1)
            {
                _delay = delay;
                _failAt = failAt;
            }

            internal List<int> Positions { get; } = new();

            internal int Peak { get; private set; }

            public async Task<PageUpload> UploadAsync(
                SiteSession session,
                long projectId,
                long projectItemId,
                int position,
                string filePath,
                int width = 0,
                int height = 0,
                CancellationToken cancellationToken = default)
            {
                lock (_gate)
                {
                    Positions.Add(position);
                    _inFlight++;
                    if (_inFlight > Peak)
                        Peak = _inFlight;
                }

                await Task.Delay(_delay, cancellationToken);

                lock (_gate)
                    _inFlight--;

                return position == _failAt
                    ? PageUpload.Refused(UploadFailureKind.File, "Сайт не принял файл: страница " + (position + 1) + ".")
                    : PageUpload.Accepted;
            }
        }

        /// <summary>An uploader whose refused page waits until the page after it has started.</summary>
        private sealed class GatedUpload : ISiteFileUploadService
        {
            public Task<PageUpload> CopyAsync(
                SiteSession session,
                long projectId,
                long projectItemId,
                int position,
                long imageId,
                string fileName,
                int width = 0,
                int height = 0,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(PageUpload.AcceptedImage(imageId));

            private readonly TaskCompletionSource<bool> _nextStarted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly object _gate = new();
            private readonly int _failAt;

            internal GatedUpload(int failAt)
            {
                _failAt = failAt;
            }

            internal List<int> Positions { get; } = new();

            public async Task<PageUpload> UploadAsync(
                SiteSession session,
                long projectId,
                long projectItemId,
                int position,
                string filePath,
                int width = 0,
                int height = 0,
                CancellationToken cancellationToken = default)
            {
                int started;
                lock (_gate)
                {
                    Positions.Add(position);
                    started = Positions.Count;
                }

                if (position == _failAt)
                {
                    await _nextStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

                    return PageUpload.Refused(
                        UploadFailureKind.File,
                        "Сайт не принял файл: страница " + (position + 1) + ".");
                }

                if (started >= _failAt + 2)
                    _nextStarted.TrySetResult(true);

                return PageUpload.Accepted;
            }
        }

        private sealed class StubUpload : ISiteFileUploadService
        {
            private readonly int _failAt;
            private readonly CancellationTokenSource? _cancelAfter;
            private readonly UploadFailureKind _refusal;
            private readonly string _error;

            /// <summary>The number the site names for every accepted picture; zero names none.</summary>
            internal long NamedImageId { get; init; }

            /// <summary>Whether the site refuses to copy a stored picture.</summary>
            internal bool RefuseCopies { get; init; }

            internal StubUpload(
                int failAt = -1,
                CancellationTokenSource? cancelAfter = null,
                UploadFailureKind refusal = UploadFailureKind.File,
                string error = "Сайт не принял файл: страница 4.")
            {
                _failAt = failAt;
                _cancelAfter = cancelAfter;
                _refusal = refusal;
                _error = error;
            }

            internal List<int> Positions { get; } = new();

            internal List<string> Paths { get; } = new();

            internal List<long> ItemIds { get; } = new();

            internal List<(int Position, long ImageId)> Copies { get; } = new();

            public Task<PageUpload> CopyAsync(
                SiteSession session,
                long projectId,
                long projectItemId,
                int position,
                long imageId,
                string fileName,
                int width = 0,
                int height = 0,
                CancellationToken cancellationToken = default)
            {
                Copies.Add((position, imageId));

                return Task.FromResult(RefuseCopies
                    ? PageUpload.Refused(UploadFailureKind.Parameters, "Сайт не принял параметры запроса.")
                    : PageUpload.AcceptedImage(imageId));
            }

            public Task<PageUpload> UploadAsync(
                SiteSession session,
                long projectId,
                long projectItemId,
                int position,
                string filePath,
                int width = 0,
                int height = 0,
                CancellationToken cancellationToken = default)
            {
                Positions.Add(position);
                Paths.Add(filePath);
                ItemIds.Add(projectItemId);

                if (position == _failAt)
                    return Task.FromResult(PageUpload.Refused(_refusal, _error));

                _cancelAfter?.Cancel();

                return Task.FromResult(NamedImageId > 0
                    ? PageUpload.AcceptedImage(NamedImageId)
                    : PageUpload.Accepted);
            }
        }
    }
}
