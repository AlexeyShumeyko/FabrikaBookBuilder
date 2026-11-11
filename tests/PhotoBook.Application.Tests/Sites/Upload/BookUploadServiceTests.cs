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
            Assert.Equal(new[] { 0, 1, 2, 3 }, upload.Positions);

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

            public int Accepted(SiteRegion region, long projectId, int filesTotal) => _start;

            public Result Remember(UploadSession session)
            {
                if (_failWrites)
                    return Result.Fail("Диск недоступен.");

                Written.Add(session.FilesUploaded);
                return Result.Ok();
            }

            public Result Forget(SiteRegion region, long projectId)
            {
                Forgotten++;
                return Result.Ok();
            }
        }

        private sealed class StubUpload : ISiteFileUploadService
        {
            private readonly int _failAt;
            private readonly CancellationTokenSource? _cancelAfter;
            private readonly UploadFailureKind _refusal;
            private readonly string _error;

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

            public Task<PageUpload> UploadAsync(
                SiteSession session,
                long projectId,
                int position,
                string filePath,
                CancellationToken cancellationToken = default)
            {
                Positions.Add(position);
                Paths.Add(filePath);

                if (position == _failAt)
                    return Task.FromResult(PageUpload.Refused(_refusal, _error));

                _cancelAfter?.Cancel();
                return Task.FromResult(PageUpload.Accepted);
            }
        }
    }
}
