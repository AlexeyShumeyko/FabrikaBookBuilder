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
                var results = new List<UploadGroupResult>();

                foreach (var created in plan.Groups)
                {
                    if (created.HasProject)
                    {
                        projects[created.Number] = created.ProjectId;
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

                Step = UploadStep.Uploading;

                foreach (var group in _groups)
                {
                    if (!projects.TryGetValue(group.Number, out var projectId))
                        continue;

                    var sent = await SendAsync(session, group, projectId, cancel.Token).ConfigureAwait(true);

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
                            new ProjectExpectation(new[] { result.Total }),
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

        /// <summary>Stops the queue between files; what the site already has stays there.</summary>
        public void Cancel() => _cancel?.Cancel();

        /// <summary>One group's pages, with the stop reason when there is one.</summary>
        private async Task<(UploadGroupResult Result, StopReason? Stop)> SendAsync(
            SiteSession session,
            UploadGroup group,
            long projectId,
            CancellationToken cancellationToken)
        {
            var progress = new Progress<UploadProgress>(report =>
            {
                Uploaded = _completedBeforeGroup + report.FilesUploaded;
                ProgressValue = Total == 0 ? 0 : Math.Min(1, (double)Uploaded / Total);
                ProgressText = string.IsNullOrEmpty(report.CurrentFile)
                    ? "Отправлено " + Uploaded + " из " + Total + "."
                    : "Отправляем " + System.IO.Path.GetFileName(report.CurrentFile) + " — " +
                        Uploaded + " из " + Total + ".";
            });

            var sent = await _upload
                .UploadAsync(session, projectId, group.Pages, progress: progress, cancellationToken: cancellationToken)
                .ConfigureAwait(true);

            // The count the site reports is the truth, whether or not a progress report made it
            // here first: a screen that only trusted callbacks would show zero after a silent run.
            Uploaded = _completedBeforeGroup + sent.Session.FilesUploaded;
            _completedBeforeGroup = Uploaded;

            var result = new UploadGroupResult(
                group.Number,
                projectId,
                sent.Session.FilesUploaded,
                group.Pages.Count,
                sent.Error is null ? Array.Empty<string>() : new[] { sent.Error });

            if (sent.IsComplete)
                return (result, null);

            if (sent.NeedsSignIn)
            {
                NeedsSignIn?.Invoke(this, EventArgs.Empty);

                return (result, new StopReason(UploadStep.Stopped, sent.Error ?? "Нужно войти заново: сеанс истёк."));
            }

            return (result, sent.WasCancelled
                ? new StopReason(UploadStep.Stopped, "Загрузка остановлена. Уже отправленное осталось на сайте.")
                : new StopReason(UploadStep.Stopped, sent.Error ?? "Сайт не принял файл."));
        }

        /// <summary>The groups that can be sent now: one book, at least one page.</summary>
        private IReadOnlyList<GroupRequest> Requests() =>
            _groups
                .Where(group => group.Group.Books.Count == 1 && group.Pages.Count > 0)
                .Select(group => new GroupRequest(group.Group, group.Request))
                .ToList();

        private int PagesOf(int number) =>
            _groups.FirstOrDefault(group => group.Number == number)?.Pages.Count ?? 0;

        private void Finish(UploadStep step, IReadOnlyList<UploadGroupResult> results, string message)
        {
            Results = results;
            Step = step;
            Message = message;
            ProgressText = string.Empty;
            ProgressValue = Total == 0 ? 0 : Math.Min(1, (double)_uploaded / Total);

            if (step == UploadStep.Done)
                ResultText = "Проект на сайте" + (HasUrl ? ": " + Url : " создан, ссылку сайт не назвал.") + ".";

            _completedBeforeGroup = 0;

            OnPropertyChanged(nameof(HasResult));
        }

        /// <summary>Why a run ended early, and what the screen says about it.</summary>
        private readonly record struct StopReason(UploadStep Step, string Message);
    }
}
