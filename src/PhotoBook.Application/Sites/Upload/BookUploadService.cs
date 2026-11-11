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
    /// </remarks>
    public sealed class BookUploadService : IBookUploadService
    {
        private readonly ISiteFileUploadService _upload;

        public BookUploadService(ISiteFileUploadService upload)
        {
            _upload = upload ?? throw new ArgumentNullException(nameof(upload));
        }

        public async Task<BookUploadResult> UploadAsync(
            SiteSession session,
            long projectId,
            IReadOnlyList<PageFile> files,
            int alreadyUploaded = 0,
            IProgress<UploadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (session is null)
                throw new ArgumentNullException(nameof(session));
            if (files is null)
                throw new ArgumentNullException(nameof(files));
            if (projectId <= 0)
                throw new ArgumentOutOfRangeException(nameof(projectId), "У проекта нет номера.");

            var uploaded = Math.Clamp(alreadyUploaded, 0, files.Count);

            for (var position = uploaded; position < files.Count; position++)
            {
                var file = files[position];
                if (file is null)
                    continue;

                if (cancellationToken.IsCancellationRequested)
                {
                    return Result(
                        UploadState.Cancelled,
                        session,
                        projectId,
                        files.Count,
                        uploaded,
                        "Загрузка отменена.");
                }

                progress?.Report(new UploadProgress(files.Count, uploaded, file.Path));

                var sent = await _upload
                    .UploadAsync(session, projectId, position, file.Path, cancellationToken)
                    .ConfigureAwait(false);

                if (!sent.IsSuccess)
                {
                    return Result(
                        UploadState.Failed,
                        session,
                        projectId,
                        files.Count,
                        uploaded,
                        sent.Error ?? "Сайт не принял файл.");
                }

                uploaded = position + 1;
                progress?.Report(new UploadProgress(files.Count, uploaded, string.Empty));
            }

            return Result(UploadState.Completed, session, projectId, files.Count, uploaded, null);
        }

        private static BookUploadResult Result(
            UploadState state,
            SiteSession session,
            long projectId,
            int total,
            int uploaded,
            string? error) =>
            new(
                state,
                new UploadSession(session.Region, projectId, total, uploaded),
                error);
    }
}
