using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application;
using PhotoBook.Application.Sites.Books;

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
    /// <para>
    /// How it ended is written to the journal: when a customer says the upload failed, the
    /// entry naming the project, the page and the file is the only account support has. The
    /// path itself is not written down - the file name and the position are enough to find it.
    /// </para>
    /// </remarks>
    public sealed class BookUploadService : IBookUploadService
    {
        private const string Source = "Отправка на сайт";

        private readonly ISiteFileUploadService _upload;
        private readonly IAcceptedFilesStore? _accepted;
        private readonly IJournal? _journal;

        public BookUploadService(
            ISiteFileUploadService upload,
            IAcceptedFilesStore? accepted = null,
            IJournal? journal = null)
        {
            _upload = upload ?? throw new ArgumentNullException(nameof(upload));
            _accepted = accepted;
            _journal = journal;
        }

        public async Task<BookUploadResult> UploadAsync(
            SiteSession session,
            long projectId,
            IReadOnlyList<PageFile> files,
            int? alreadyUploaded = null,
            IProgress<UploadProgress>? progress = null,
            long projectItemId = 0,
            CancellationToken cancellationToken = default)
        {
            if (session is null)
                throw new ArgumentNullException(nameof(session));
            if (files is null)
                throw new ArgumentNullException(nameof(files));
            if (projectId <= 0)
                throw new ArgumentOutOfRangeException(nameof(projectId), "У проекта нет номера.");

            var start = alreadyUploaded
                ?? _accepted?.Accepted(session.Region, projectId, projectItemId, files.Count)
                ?? 0;
            var uploaded = Math.Clamp(start, 0, files.Count);
            string? notice = null;

            for (var position = uploaded; position < files.Count; position++)
            {
                var file = files[position];
                if (file is null)
                    continue;

                if (cancellationToken.IsCancellationRequested)
                {
                    _journal?.Write(
                        JournalLevel.Information,
                        Source,
                        $"Отправка отменена: проект {projectId}, отправлено {uploaded} из {files.Count}.");

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
                    .UploadAsync(
                        session,
                        projectId,
                        projectItemId,
                        position,
                        file.Path,
                        file.Width,
                        file.Height,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!page.IsAccepted)
                {
                    _journal?.Write(
                        JournalLevel.Warning,
                        Source,
                        $"Книга не отправлена: проект {projectId}, файл {position + 1} из {files.Count} «{Path.GetFileName(file.Path)}»: {page.Error ?? "Сайт не принял файл."}");

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
                notice = Remember(session.Region, projectId, projectItemId, files.Count, uploaded) ?? notice;

                progress?.Report(new UploadProgress(files.Count, uploaded, string.Empty));
            }

            if (uploaded >= files.Count && _accepted is not null &&
                !_accepted.Forget(session.Region, projectId, projectItemId).IsSuccess)
            {
                notice = "Книга загружена, но запись о ней не удалилась — при следующей отправке она может быть пропущена.";
            }

            _journal?.Write(
                JournalLevel.Information,
                Source,
                $"Книга отправлена: проект {projectId}, файлов {files.Count}.");

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
        private string? Remember(SiteRegion region, long projectId, long itemId, int total, int uploaded)
        {
            if (_accepted is null)
                return null;

            var written = _accepted.Remember(new UploadSession(region, projectId, total, uploaded, itemId));

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
