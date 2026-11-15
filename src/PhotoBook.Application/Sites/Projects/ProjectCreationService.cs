using System;
using System.Collections.Generic;
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
                        $"Группа уже отправлена раньше: проект {known}, книг {group.Group.Books.Count}.");

                    outcomes.Add(new GroupCreation(
                        number,
                        group.Group,
                        GroupCreationState.AlreadyCreated,
                        known,
                        null));
                    continue;
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

                outcomes.Add(new GroupCreation(
                    number,
                    group.Group,
                    GroupCreationState.Created,
                    project.Id,
                    remembered.IsSuccess
                        ? null
                        : "Проект создан, но запись об этом не сохранилась — при повторной отправке он может создаться снова."));
            }

            return new ProjectCreationPlan(outcomes);
        }
    }
}
