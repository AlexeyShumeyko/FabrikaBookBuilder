using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhotoBook.Application;

namespace PhotoBook.Application.Sites.Projects
{
    /// <summary>Creates projects group by group and keeps the local record in step.</summary>
    /// <remarks>
    /// <para>
    /// The record is written straight after the site answers, before the next group is
    /// attempted: a crash between those two moments would otherwise lose the only thing that
    /// knows the project exists, and the retry would make a second one. When the record cannot
    /// be written the project is reported as created with that warning, because it is true
    /// that the project exists and untrue to say the next attempt is safe.
    /// </para>
    /// <para>
    /// A failed group does not stop the others: each is a separate project, and the customer
    /// gets the ones that worked. The plan names the ones that did not, and a second run skips
    /// the finished ones because their keys are in the record.
    /// </para>
    /// <para>
    /// Each outcome is also written to the journal. A project that was created but not recorded,
    /// or not created at all, is exactly the case a customer reports as "half of my books went
    /// to the site", and the entry names the group and the number of books it held.
    /// </para>
    /// <para>
    /// A project is not finished when it exists: the site keeps its parameters and its price
    /// only because the client sends them, and a project without them is one the site prices at
    /// zero. So the same pass names them, and a project the parameters did not reach is
    /// reported even though it was created - the customer would otherwise find an order on the
    /// site that cannot be produced.
    /// </para>
    /// </remarks>
    public sealed class ProjectCreationService : IProjectCreationService
    {
        private const string Source = "Создание проекта";

        private readonly ISiteProjectService _projects;
        private readonly ICreatedProjectStore _created;
        private readonly IJournal? _journal;

        public ProjectCreationService(
            ISiteProjectService projects,
            ICreatedProjectStore created,
            IJournal? journal = null)
        {
            _projects = projects ?? throw new ArgumentNullException(nameof(projects));
            _created = created ?? throw new ArgumentNullException(nameof(created));
            _journal = journal;
        }

        public async Task<ProjectCreationPlan> CreateAsync(
            SiteSession session,
            IReadOnlyList<GroupRequest> groups,
            CancellationToken cancellationToken = default)
        {
            if (session is null)
                throw new ArgumentNullException(nameof(session));
            if (groups is null)
                throw new ArgumentNullException(nameof(groups));

            var outcomes = new List<GroupCreation>(groups.Count);

            for (var index = 0; index < groups.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var number = index + 1;
                var group = groups[index] ?? throw new ArgumentException(
                    "The order holds a group that is not there.",
                    nameof(groups));

                var key = GroupKey.For(session.Region, group.Group.Books);

                if (_created.Find(key) is { } known)
                {
                    _journal?.Write(
                        JournalLevel.Information,
                        Source,
                        $"Группа найдена в записи: проект {known}, книг {group.Group.Books.Count}.");

                    // The parameters are written again for a project found in the record: a run
                    // that stopped before naming them left exactly such a project behind, and
                    // naming them twice costs one request and changes nothing.
                    var knownBooks = await BookIdsAsync(session, known, group.Group.Books.Count, null, cancellationToken)
                        .ConfigureAwait(false);

                    if (!knownBooks.Unreadable)
                    {
                        var knownNamed = knownBooks.Error is null
                            ? await SetUpAsync(session, known, group, cancellationToken).ConfigureAwait(false)
                            : null;

                        outcomes.Add(new GroupCreation(
                            number,
                            group.Group,
                            GroupCreationState.AlreadyCreated,
                            known,
                            Join(knownBooks.Error, knownNamed),
                            knownBooks.Items));
                        continue;
                    }

                    // The record names a project the site will not describe - deleted by hand,
                    // or a record from another account. The customer asked for these books to
                    // be uploaded, so a new project is made rather than the run stopped with an
                    // answer nobody can act on.
                    _journal?.Write(
                        JournalLevel.Warning,
                        Source,
                        $"Проект {known} записан, но его книги не прочитались: {knownBooks.Error} Создаём новый проект.");
                }

                var created = await _projects
                    .CreateAsync(session, group.Request, cancellationToken)
                    .ConfigureAwait(false);

                if (!created.IsSuccess || created.Value is null)
                {
                    _journal?.Write(
                        JournalLevel.Warning,
                        Source,
                        $"Проект не создан: книг {group.Group.Books.Count}, сайт ответил: {created.Error ?? "без причины"}");

                    outcomes.Add(new GroupCreation(
                        number,
                        group.Group,
                        GroupCreationState.Failed,
                        0,
                        created.Error ?? "Сайт не создал проект."));
                    continue;
                }

                var project = created.Value;
                var remembered = _created.Remember(key, project.Id);

                _journal?.Write(
                    JournalLevel.Information,
                    Source,
                    $"Проект {project.Id} создан: книг {group.Group.Books.Count}.");

                var books = await BookIdsAsync(session, project.Id, group.Group.Books.Count, project, cancellationToken)
                    .ConfigureAwait(false);

                var named = books.Error is null
                    ? await SetUpAsync(session, project.Id, group, cancellationToken).ConfigureAwait(false)
                    : null;

                var notRecorded = remembered.IsSuccess
                    ? null
                    : "Проект создан, но запись об этом не сохранилась — при повторной отправке он может создаться снова.";

                outcomes.Add(new GroupCreation(
                    number,
                    group.Group,
                    GroupCreationState.Created,
                    project.Id,
                    Join(notRecorded, Join(books.Error, named)),
                    books.Items));
            }

            return new ProjectCreationPlan(outcomes);
        }

        /// <summary>
        /// The site's number for every book of the group, adding the ones the project lacks.
        /// </summary>
        /// <remarks>
        /// The site creates a project with one book, so a group of three needs two more. Adding
        /// them one position at a time is the only way the site offers, and a book that did not
        /// arrive is reported rather than skipped: a project with fewer books would be priced
        /// for a group it does not hold.
        /// </remarks>
        private async Task<(IReadOnlyList<long> Items, string? Error, bool Unreadable)> BookIdsAsync(
            SiteSession session,
            long projectId,
            int books,
            SiteProject? created,
            CancellationToken cancellationToken)
        {
            var ids = new List<long>(books);

            if (created is not null && created.FirstItemId > 0)
            {
                ids.Add(created.FirstItemId);
            }
            else
            {
                // A project an earlier run made: its books are read back, not guessed.
                var read = await _projects.ReadAsync(session, projectId, cancellationToken).ConfigureAwait(false);

                if (!read.IsSuccess || read.Value is null)
                {
                    return (ids, "Проект уже был создан, но сайт не отдал его книги: " +
                        (read.Error ?? "без причины") + ".", true);
                }

                ids.AddRange(read.Value.Items.Where(item => item.Id > 0).Select(item => item.Id));
            }

            while (ids.Count < books)
            {
                var position = ids.Count + 1;
                var added = await _projects.AddBookAsync(session, projectId, position, cancellationToken)
                    .ConfigureAwait(false);

                if (!added.IsSuccess || added.Value <= 0)
                {
                    _journal?.Write(
                        JournalLevel.Warning,
                        Source,
                        $"В проект {projectId} не добавлена книга {position} из {books}: " +
                        (added.Error ?? "сайт не назвал книгу"));

                    return (ids, $"В проект не удалось добавить книгу {position} из {books}: " +
                        (added.Error ?? "сайт не назвал книгу") + ".", false);
                }

                ids.Add(added.Value);

                _journal?.Write(
                    JournalLevel.Information,
                    Source,
                    $"В проект {projectId} добавлена книга {position} из {books}: {added.Value}.");
            }

            if (ids.Count > books)
            {
                return (ids.Take(books).ToList(),
                    $"В проекте на сайте больше книг, чем в группе: {ids.Count} вместо {books}. Отправка остановлена.", false);
            }

            return (ids, null, false);
        }

        /// <summary>
        /// Names the project's parameters and price, or says why the site did not take them.
        /// </summary>
        /// <remarks>
        /// A group the site did not price is left alone: the plan already refuses such books, and
        /// a project priced at zero would be worse than one the customer sees is unfinished.
        /// </remarks>
        private async Task<string?> SetUpAsync(
            SiteSession session,
            long projectId,
            GroupRequest group,
            CancellationToken cancellationToken)
        {
            var parameters = ProjectParameters.For(group.Group.Books[0], group.Group.Books.Count, group.Price);
            if (parameters is null)
                return null;

            var named = await _projects
                .SetUpAsync(session, projectId, group.Request.ProductId, parameters, cancellationToken)
                .ConfigureAwait(false);

            if (named.IsSuccess)
            {
                _journal?.Write(
                    JournalLevel.Information,
                    Source,
                    $"Параметры и цена записаны в проект {projectId}: книг {parameters.Books}, " +
                    $"цена {parameters.Price.ToString(CultureInfo.InvariantCulture)}.");

                return null;
            }

            var reason = named.Error ?? "без причины";

            _journal?.Write(
                JournalLevel.Warning,
                Source,
                $"Сайт не принял параметры проекта {projectId}: {reason}");

            return "Проект создан, но сайт не принял его параметры и цену: " + reason;
        }

        /// <summary>Both things that went wrong, in one sentence the screen can show.</summary>
        private static string? Join(string? first, string? second)
        {
            if (string.IsNullOrWhiteSpace(first))
                return string.IsNullOrWhiteSpace(second) ? null : second;

            if (string.IsNullOrWhiteSpace(second))
                return first;

            return first + " " + second;
        }
    }
}
