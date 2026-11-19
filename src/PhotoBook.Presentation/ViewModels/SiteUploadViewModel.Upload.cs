using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Books.Submission;
using PhotoBook.Application.Sites.Completion;
using PhotoBook.Application.Sites.Projects;
using PhotoBook.Application.Sites.Upload;

namespace PhotoBook.Presentation.ViewModels
{
    /// <summary>The run itself: create the projects, send the pages, read back what arrived.</summary>
    public sealed partial class SiteUploadViewModel
    {
        /// <summary>Creates the projects, sends the pages, and reads back what arrived.</summary>
        public async Task UploadAsync(CancellationToken cancellationToken = default)
        {
            if (!CanUpload || _session is null || _summary is null)
                return;

            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancel = cancel;

            Message = string.Empty;
            ResultText = string.Empty;
            Url = string.Empty;
            Results = Array.Empty<UploadGroupResult>();
            Uploaded = 0;
            ProgressValue = 0;

            var session = _session;

            try
            {
                Step = UploadStep.Creating;
                ProgressText = "Создаём проекты на сайте.";

                var plan = await _creation
                    .CreateAsync(session, Requests(), cancel.Token)
                    .ConfigureAwait(true);

                var projects = new Dictionary<int, long>();
                var books = new Dictionary<int, IReadOnlyList<long>>();
                var results = new List<UploadGroupResult>();

                foreach (var created in plan.Groups)
                {
                    if (created.HasProject)
                    {
                        projects[created.Number] = created.ProjectId;
                        books[created.Number] = created.BookIds;
                    }
                    else
                    {
                        results.Add(new UploadGroupResult(
                            created.Number,
                            0,
                            0,
                            PagesOf(created.Number),
                            new[] { created.Error ?? "Сайт не создал проект." }));
                    }
                }

                if (projects.Count == 0)
                {
                    Finish(UploadStep.Stopped, results, "Сайт не создал ни одного проекта. Проверьте форму и попробуйте снова.");
                    return;
                }

                // A project the site holds without its parameters and price cannot be produced,
                // so the run stops before sending pages into it: an order the customer cannot
                // order is worse than a stopped run, and nothing has been sent yet.
                var unconfigured = plan.Groups
                    .FirstOrDefault(group => group.HasProject && !string.IsNullOrWhiteSpace(group.Error));

                if (unconfigured is not null)
                {
                    Finish(UploadStep.Stopped, results, unconfigured.Error!);
                    return;
                }

                Step = UploadStep.Uploading;

                foreach (var group in _groups)
                {
                    if (!projects.TryGetValue(group.Number, out var projectId))
                        continue;

                    var sent = await SendAsync(
                            session,
                            group,
                            projectId,
                            books.TryGetValue(group.Number, out var ids) ? ids : Array.Empty<long>(),
                            cancel.Token)
                        .ConfigureAwait(true);

                    results.Add(sent.Result);

                    if (sent.Stop is { } stop)
                    {
                        Finish(stop.Step, results, stop.Message);
                        return;
                    }
                }

                Step = UploadStep.Checking;
                ProgressText = "Сверяем проекты с сайтом.";

                var checkedResults = new List<UploadGroupResult>(results.Count);
                string? link = null;

                foreach (var result in results)
                {
                    if (result.ProjectId <= 0)
                    {
                        checkedResults.Add(result);
                        continue;
                    }

                    var comparison = await _check
                        .CheckAsync(
                            session,
                            result.ProjectId,
                            new ProjectExpectation(BooksOf(result.Number, result.Total)),
                            cancel.Token)
                        .ConfigureAwait(true);

                    if (!comparison.IsSuccess || comparison.Value is null)
                    {
                        checkedResults.Add(result with
                        {
                            Problems = new[] { comparison.Error ?? "Сайт не отдал проект для сверки." }
                        });
                        continue;
                    }

                    var check = comparison.Value;
                    link ??= check.HasUrl ? check.Url : null;

                    checkedResults.Add(result with { Problems = check.Differences });
                }

                if (checkedResults.All(result => result.IsComplete))
                {
                    Url = link ?? string.Empty;

                    // A finished run forgets what it created, so pressing «Загрузить на сайт»
                    // again makes a new project rather than stopping on the old one. The
                    // record still protects a run that stopped halfway, where the same press
                    // is a retry.
                    ForgetCreatedProjects(session);
                    RememberFinishedGroups(session, checkedResults);

                    Finish(
                        UploadStep.Done,
                        checkedResults,
                        "Всё загружено. Проект сохранён на сайте, в корзину ничего не добавлялось.");
                }
                else
                {
                    Finish(
                        UploadStep.Stopped,
                        checkedResults,
                        "Загрузка закончилась, но сайт держит не всё, что отправлено. Подробности ниже.");
                }
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                Finish(UploadStep.Stopped, Results, "Загрузка остановлена. Уже отправленное осталось на сайте.");
            }
            finally
            {
                _cancel = null;
            }
        }

        /// <summary>Leaves the screen for the order behind it, unless a run is writing.</summary>
        public void Back()
        {
            if (IsBusy)
                return;

            BackRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Stops the queue between files; what the site already has stays there.</summary>
        public void Cancel() => _cancel?.Cancel();


        /// <summary>One group's books, with the stop reason when there is one.</summary>
        /// <remarks>
        /// The books go one after another, each against the number the site gave it: files of
        /// one book are numbered from the cover, so sending them under another book's number
        /// would replace that book's pages instead of adding a new one.
        /// </remarks>
        private async Task<(UploadGroupResult Result, StopReason? Stop)> SendAsync(
            SiteSession session,
            UploadGroup group,
            long projectId,
            IReadOnlyList<long> bookIds,
            CancellationToken cancellationToken)
        {
            var sentBeforeBook = 0;

            for (var index = 0; index < group.Group.Books.Count; index++)
            {
                var book = group.Group.Books[index];
                var pages = book.AllFiles;
                if (pages.Count == 0)
                    continue;

                var itemId = index < bookIds.Count ? bookIds[index] : 0;

                // Files of the group before this book. The count reported by the book is its
                // own, from zero, so it is added to this offset and to nothing else: the offset
                // used to be advanced inside the same loop, and the second book of an order
                // started from twice the files already sent - the bar filled at two thirds and
                // the caption counted past the order itself.
                var offset = _completedBeforeGroup + sentBeforeBook;

                var progress = new Progress<UploadProgress>(report => Show(offset + report.FilesUploaded, report.CurrentFile));

                var sent = await _upload
                    .UploadAsync(
                        session,
                        projectId,
                        pages,
                        progress: progress,
                        cancellationToken: cancellationToken,
                        projectItemId: itemId)
                    .ConfigureAwait(true);

                // The count the site reports is the truth, whether or not a progress report made
                // it here first: a screen that only trusted callbacks would show zero after a
                // silent run.
                sentBeforeBook += sent.Session.FilesUploaded;
                Show(_completedBeforeGroup + sentBeforeBook, null);

                if (sent.IsComplete)
                    continue;

                var failed = new UploadGroupResult(
                    group.Number,
                    projectId,
                    sentBeforeBook,
                    group.Pages.Count,
                    sent.Error is null ? Array.Empty<string>() : new[] { sent.Error });

                return (failed, StopOf(sent));
            }

            _completedBeforeGroup += sentBeforeBook;

            var result = new UploadGroupResult(
                group.Number,
                projectId,
                sentBeforeBook,
                group.Pages.Count,
                Array.Empty<string>());

            return (result, null);
        }

        /// <summary>
        /// Shows how far the run is, never going back and never past the order.
        /// </summary>
        /// <remarks>
        /// Reports arrive on the window's thread in the order they were made, but a report from
        /// a book that has just finished can still land after the next book's: the count only
        /// moves forward, and the order's own number of files is the ceiling.
        /// </remarks>
        private void Show(int files, string? currentFile)
        {
            var capped = Total > 0 ? Math.Min(Total, files) : files;

            if (capped > _uploaded)
                Uploaded = capped;

            ProgressValue = Total == 0 ? 0 : Math.Min(100, 100.0 * _uploaded / Total);

            if (string.IsNullOrEmpty(currentFile))
            {
                ProgressText = "Отправлено " + _uploaded + " из " + Total + ".";
                return;
            }

            ProgressText = "Отправляем " + System.IO.Path.GetFileName(currentFile) + " — " +
                _uploaded + " из " + Total + ".";
        }

        /// <summary>Why a book of the group stopped, in terms of the screen.</summary>
        private StopReason StopOf(BookUploadResult sent)
        {
            if (sent.NeedsSignIn)
            {
                // The token the screen holds is the one the site just refused, so it is dropped
                // before the password is asked for: a button that kept it would offer to try
                // again with the same dead token.
                _session = null;
                OnPropertyChanged(nameof(CanUpload));

                NeedsSignIn?.Invoke(this, EventArgs.Empty);

                return new StopReason(UploadStep.Stopped, sent.Error ?? "Нужно войти заново: сеанс истёк.");
            }

            return sent.WasCancelled
                ? new StopReason(UploadStep.Stopped, "Загрузка остановлена. Уже отправленное осталось на сайте.")
                : new StopReason(UploadStep.Stopped, sent.Error ?? "Сайт не принял файл.");
        }

        /// <summary>
        /// What each book of a project should look like on the site, so the check reads book by book.
        /// </summary>
        /// <remarks>
        /// The site keeps the books apart inside a project, and the cover apart from the spreads,
        /// so a run of three books has to be read back as three books with their covers counted
        /// separately: one total would make a project of three short books look like one long book
        /// and hide a book that lost its cover.
        /// </remarks>
        private IReadOnlyList<ExpectedBook> BooksOf(int number, int total)
        {
            var group = _groups.FirstOrDefault(item => item.Number == number);
            if (group is null || group.Group.Books.Count == 0)
                return new[] { new ExpectedBook(false, total) };

            var books = new List<ExpectedBook>(group.Group.Books.Count);
            foreach (var book in group.Group.Books)
                books.Add(new ExpectedBook(book.CoverFile is not null, book.Files?.Count ?? 0));

            return books;
        }

        /// <summary>Forgets the record of the groups a finished run sent.</summary>
        private void ForgetCreatedProjects(SiteSession session)
        {
            if (_created is null)
                return;

            foreach (var group in _groups)
                _created.Forget(GroupKey.For(session.Region, group.Group.Books));
        }

        /// <summary>
        /// Writes down which project each finished group went to, for the order page to show.
        /// </summary>
        /// <remarks>
        /// This is not the created-project record: that one is deliberately dropped on success so
        /// the next press makes a new project. This one only says «these books were uploaded
        /// already, project N», which is what the customer needs to see before sending again.
        /// </remarks>
        private void RememberFinishedGroups(
            SiteSession session,
            IReadOnlyList<UploadGroupResult> results)
        {
            if (_uploadedProjects is null)
                return;

            foreach (var group in _groups)
            {
                var finished = results.FirstOrDefault(item =>
                    item.Number == group.Number && item.ProjectId > 0);

                if (finished is null)
                    continue;

                _uploadedProjects.RememberUploaded(
                    GroupKey.For(session.Region, group.Group.Books), finished.ProjectId);
            }
        }

        /// <summary>The groups that can be sent now: at least one book with at least one page.</summary>
        private IReadOnlyList<GroupRequest> Requests() =>
            _groups
                .Where(group => group.Group.Books.Count > 0 && group.Pages.Count > 0)
                .Select(group => new GroupRequest(group.Group, group.Request, group.Price))
                .ToList();

        private int PagesOf(int number) =>
            _groups.FirstOrDefault(group => group.Number == number)?.Pages.Count ?? 0;

        private void Finish(UploadStep step, IReadOnlyList<UploadGroupResult> results, string message)
        {
            Results = results;
            Step = step;
            Message = message;
            ProgressText = string.Empty;
            ProgressValue = Total == 0 ? 0 : Math.Min(100, 100.0 * _uploaded / Total);

            if (step == UploadStep.Done)
                ResultText = "Проект на сайте" + (HasUrl ? ": " + Url : " создан, ссылку сайт не назвал.") + ".";

            _completedBeforeGroup = 0;

            OnPropertyChanged(nameof(HasResult));
        }

        /// <summary>Why a run ended early, and what the screen says about it.</summary>
        private readonly record struct StopReason(UploadStep Step, string Message);
    }
}
