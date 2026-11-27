using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites.Books;
using PhotoBook.Application.Sites.Books.Files;
using PhotoBook.Application.Sites.Upload;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>The file half of one group: what is wrong with its files, book by book.</summary>
    /// <remarks>
    /// <para>
    /// The reading belongs to the group because the group's chosen format decides what the site
    /// asks of a page: two groups of the same project can hold files of two sizes, and one
    /// reading over both would either refuse the right book or accept the wrong one.
    /// </para>
    /// <para>
    /// The reading runs by itself once the group has a plan, because a list of books that says
    /// nothing about their files is a list the customer has to guess from. A file the site would
    /// print cropped stops the group like any other refusal: the group names it on the card and
    /// on the book's row, and the way forward is another file or another format - never a box to
    /// tick, which could not make the page the size the site prints.
    /// </para>
    /// </remarks>
    public sealed partial class SiteOrderGroup
    {
        /// <summary>The plan whose files are still to be read.</summary>
        private UploadPlan? _pendingPlan;

        /// <summary>Starts a reading of the files for a new plan, dropping the one before it.</summary>
        /// <remarks>
        /// A price that came back while a reading was still running must not be lost: the
        /// reading in progress is stopped, and the newest plan is read after it, so the group
        /// never keeps the status of an order it no longer holds.
        /// </remarks>
        private void StartFileCheck(UploadPlan plan)
        {
            _pendingPlan = plan;
            _cancelFiles?.Cancel();

            if (!IsCheckingFiles)
                _filesRun = RunChecksAsync();
        }

        private async Task RunChecksAsync()
        {
            while (_pendingPlan is { } plan)
            {
                _pendingPlan = null;
                await CheckFilesAsync(plan).ConfigureAwait(true);
            }
        }

        private async Task CheckFilesAsync(UploadPlan plan, CancellationToken cancellationToken = default)
        {
            if (_fileCheck is null || _sessions is null)
                return;

            var session = _sessions.Load();
            if (session is null)
            {
                // Nothing to check against: the catalogue's sizes come with the session.
                _filesProblem = "Файлы проверим после входа на сайт.";
                FilesText = string.Empty;
                NotifyProblem();
                return;
            }

            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancelFiles = cancel;
            _pendingPlan = null;

            _filesProblem = null;
            IsCheckingFiles = true;
            FilesText = "Читаем файлы книги.";

            try
            {
                // Reported inline rather than through Progress<T>: that one posts to the
                // dispatcher, so the last "проверяем книгу" line would land after the answer and
                // overwrite it. The awaits here already resume on the window's thread.
                var progress = new InlineProgress<FileCheckProgress>(report =>
                {
                    FilesText = report.BookNumber == 0
                        ? "Читаем файлы книги."
                        : "Проверяем книгу " + report.BookNumber + " из " + report.BooksTotal + ".";
                });

                var check = await _fileCheck
                    .CheckAsync(plan.Groups, progress, cancel.Token)
                    .ConfigureAwait(true);

                Files = check;
                FilesText = string.Empty;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                Files = null;
                FilesText = string.Empty;
            }
            catch (Exception error)
            {
                Files = new OrderFileCheck(
                    Array.Empty<CheckedBook>(),
                    new[]
                    {
                        new FileAlert(FileAlertLevel.Blocking, null, "Файлы не проверены: " + error.Message)
                    });

                FilesText = string.Empty;
            }
            finally
            {
                if (ReferenceEquals(_cancelFiles, cancel))
                    _cancelFiles = null;

                IsCheckingFiles = false;
            }
        }

        /// <summary>Gives every book of the group the status the check found for it.</summary>
        private void ApplyCheck()
        {
            var plan = _plan;
            var checks = _files?.Books ?? Array.Empty<CheckedBook>();

            // The plan's books and the answers come back in one order: the books of every project
            // the plan holds, group by group. The book's own project book is the key, so a row is
            // never given the status of a book another group sent.
            var found = new Dictionary<PhotoBook.Core.Book, (Book Book, CheckedBook Check)>(
                ReferenceEqualityComparer.Instance);

            if (plan is not null)
            {
                var books = plan.Groups.SelectMany(group => group.Group.Books).ToList();

                for (var index = 0; index < books.Count && index < checks.Count; index++)
                {
                    if (books[index].Origin is { } origin)
                        found[origin] = (books[index], checks[index]);
                }
            }

            foreach (var row in Books)
            {
                if (found.TryGetValue(row.Book, out var answer))
                    row.ShowCheck(RowOf(row, answer.Book, answer.Check));
                else
                    row.ClearCheck();
            }
        }

        /// <summary>One book with its status and the files it would send.</summary>
        private UploadBookRow RowOf(OrderBookRow row, Book book, CheckedBook check)
        {
            var status = BookStatus.Of(book, check, IsCheckingFiles);
            var files = new List<UploadFileRow>();

            if (book.CoverFile is { } cover)
                files.Add(FileRowOf("Обложка", null, cover, check));

            var spreads = book.Files ?? (IReadOnlyList<PageFile>)Array.Empty<PageFile>();
            for (var index = 0; index < spreads.Count; index++)
                files.Add(FileRowOf("Разворот " + (index + 1), index + 1, spreads[index], check));

            return new UploadBookRow(row.Number, check?.Name ?? row.Name, status, files);
        }

        /// <summary>One file, with the site's own complaint about it when there is one.</summary>
        private static UploadFileRow FileRowOf(string title, int? position, PageFile file, CheckedBook? check)
        {
            var alert = check?.Alerts.FirstOrDefault(item => item.PageNumber == position);

            return new UploadFileRow(
                title,
                System.IO.Path.GetFileName(file.Path),
                alert?.Message,
                alert?.Level);
        }

        /// <summary>Runs the report where it is made, in the order the work happens.</summary>
        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> _report;

            public InlineProgress(Action<T> report) => _report = report;

            public void Report(T value) => _report(value);
        }
    }
}
