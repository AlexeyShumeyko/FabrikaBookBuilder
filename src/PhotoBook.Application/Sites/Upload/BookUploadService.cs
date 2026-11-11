using System;
using System.Collections.Generic;
using PhotoBook.Application.Sites.Books;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoBook.Application.Sites.Upload
{
    /// <summary>Sends a book page by page, keeping count of what the site accepted.</summary>
    /// <remarks>
    /// <para>
    /// A failure stops the queue instead of pushing on: the next page would arrive out of order
    /// or against a project the site has already rejected, and the customer would get a book
    /// with a hole in the middle. What was sent stays sent, and the session says where a retry
    /// begins - that is the whole point of counting files.
    /// </para>
    /// <para>
    /// Cancelling is treated the same way as a failure except for what it means: the customer
    /// asked to stop, so it is reported as cancelled rather than as an error, and nothing is
    /// deleted from the site.
    /// </para>
    /// <para>
    /// Where the book starts is asked of the record before anything is sent, so an upload that
    /// lost its connection, or the program it ran in, continues at the next page instead of
    /// starting over. Every accepted page is written down at once, and the record is dropped when
    /// the book is whole: one that outlived its book would make a later upload skip pages.
    /// </para>
    /// </remarks>
    public sealed class BookUploadService : IBookUploadService
    {
        private readonly ISiteFileUploadService _upload;
        private readonly IAcceptedFilesStore? _accepted;

        public BookUploadService(ISiteFileUploadService upload, IAcceptedFilesStore? accepted = null)
        {
            _upload = upload ?? throw new ArgumentNullException(nameof(upload));
            _accepted = accepted;
        }

        public async Task<BookUploadResult> UploadAsync(
            SiteSession session,
            long projectId,
            IReadOnlyList<PageFile> files,
            int? alreadyUploaded = null,
            IProgress<UploadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (session is null)
                throw new ArgumentNullException(nameof(session));
            if (files is null)
                throw new ArgumentNullException(nameof(files));
            if (projectId <= 0)
                throw new ArgumentOutOfRangeException(nameof(projectId), "У проекта нет номера.");

            var start = alreadyUploaded ?? _accepted?.Accepted(session.Region, projectId, files.Count) ?? 0;
            var uploaded = Math.Clamp(start, 0, files.Count);
            string? notice = null;

            for (var position = uploaded; position < files.Count; position++)
            {
                var file = files[position];
                if (file is null)
                    continue;

                if (cancellationToken.IsCancellationRequested)
                {
                    return Ended(
                        UploadState.Cancelled,
                        session,
                        projectId,
                        files.Count,
                        uploaded,
                        UploadFailureKind.None,
                        "Загрузка отменена.",
                        notice);
                }

                progress?.Report(new UploadProgress(files.Count, uploaded, file.Path));

                var page = await _upload
                    .UploadAsync(session, projectId, position, file.Path, cancellationToken)
                    .ConfigureAwait(false);

                if (!page.IsAccepted)
                {
                    return Ended(
                        UploadState.Failed,
                        session,
                        projectId,
                        files.Count,
                        uploaded,
                        page.Kind,
                        page.Error ?? "Сайт не принял файл.",
                        notice);
                }

                uploaded = position + 1;
                notice = Remember(session.Region, projectId, files.Count, uploaded) ?? notice;

                progress?.Report(new UploadProgress(files.Count, uploaded, string.Empty));
            }

            if (uploaded >= files.Count && _accepted is not null && !_accepted.Forget(session.Region, projectId).IsSuccess)
            {
                notice = "Книга загружена, но запись о ней не удалилась — при следующей отправке она может быть пропущена.";
            }

            return Ended(
                UploadState.Completed,
                session,
                projectId,
                files.Count,
                uploaded,
                UploadFailureKind.None,
                null,
                notice);
        }

        /// <summary>Writes down how far the book got, or says why it could not.</summary>
        private string? Remember(SiteRegion region, long projectId, int total, int uploaded)
        {
            if (_accepted is null)
                return null;

            var written = _accepted.Remember(new UploadSession(region, projectId, total, uploaded));

            return written.IsSuccess
                ? null
                : "Загрузка идёт, но запись о ней не сохранилась — после перезапуска она начнётся заново.";
        }

        private static BookUploadResult Ended(
            UploadState state,
            SiteSession session,
            long projectId,
            int total,
            int uploaded,
            UploadFailureKind kind,
            string? error,
            string? notice) =>
            new(
                state,
                new UploadSession(session.Region, projectId, total, uploaded),
                error,
                kind,
                notice);
    }
}
